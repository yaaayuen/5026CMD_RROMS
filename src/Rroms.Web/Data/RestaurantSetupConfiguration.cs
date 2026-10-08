using Microsoft.EntityFrameworkCore;

using Rroms.Web.Constants;
using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>
/// Database rules (indexes, check constraints, relationships) and seed data for the
/// WL-1 restaurant setup tables.
/// </summary>
/// <remarks>
/// Kept in its own file so <see cref="ApplicationDbContext"/>, which all five members edit,
/// needs only one line for this module. Fewer shared lines means fewer merge conflicts.
/// Each rule here is also checked in the service layer; the database is the last line of defence
/// in case a bug or a direct SQL edit bypasses the service.
/// </remarks>
public static class RestaurantSetupConfiguration
{
    /// <summary>
    /// Fixed timestamp for seed rows. HasData needs constant values: DateTime.UtcNow would differ
    /// on every build, EF would think the seed data changed, and every migration would rewrite it.
    /// </summary>
    private static readonly DateTime SeedTimestampUtc = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Default opening time for the seeded week, Malaysia local time.</summary>
    private static readonly TimeOnly DefaultOpensAt = new TimeOnly(11, 0);

    /// <summary>Default closing time for the seeded week, Malaysia local time.</summary>
    private static readonly TimeOnly DefaultClosesAt = new TimeOnly(22, 0);

    /// <summary>Seed ID of the "Main Hall" dining area.</summary>
    private const int MainHallId = 1;

    /// <summary>Seed ID of the "Patio" dining area.</summary>
    private const int PatioId = 2;

    /// <summary>
    /// Applies every restaurant setup rule. Called once from ApplicationDbContext.OnModelCreating.
    /// </summary>
    /// <param name="builder">The model builder passed in by EF Core.</param>
    public static void Configure(ModelBuilder builder)
    {
        ConfigureRestaurantProfile(builder);
        ConfigureOperatingHours(builder);
        ConfigureSpecialClosures(builder);
        ConfigureDiningAreas(builder);
        ConfigureDiningTables(builder);
        ConfigureMenuCategories(builder);
    }

    /// <summary>One profile row with Id 1, seeded with placeholder contact details.</summary>
    private static void ConfigureRestaurantProfile(ModelBuilder builder)
    {
        builder.Entity<RestaurantProfile>(profile =>
        {
            // The Id is always 1 and chosen by us, so the database must not generate it.
            profile.Property(p => p.Id).ValueGeneratedNever();

            // Obvious placeholders: the administrator replaces them on the Restaurant setup page.
            profile.HasData(new RestaurantProfile
            {
                Id = RestaurantProfile.SingletonId,
                Name = SiteInfo.RestaurantName,
                Address = "1 Example Street, 50000 Kuala Lumpur (placeholder)",
                Phone = "+60 3-0000 0000",
                Email = "hello@threegrills.example",
                UpdatedAtUtc = SeedTimestampUtc
            });
        });
    }

    /// <summary>One row per weekday, opening must be before closing unless the day is closed.</summary>
    private static void ConfigureOperatingHours(ModelBuilder builder)
    {
        builder.Entity<OperatingHour>(hour =>
        {
            // Store "Monday" instead of 1, like the team's other enums, so SSMS is readable.
            hour.Property(h => h.DayOfWeek).HasConversion<string>().HasMaxLength(10);

            // Each day may appear only once.
            hour.HasIndex(h => h.DayOfWeek).IsUnique();

            hour.ToTable(table => table.HasCheckConstraint(
                "CK_OperatingHours_OpensBeforeCloses",
                "[IsClosed] = 1 OR [OpensAt] < [ClosesAt]"));

            hour.HasData(
                SeedDay(1, DayOfWeek.Monday),
                SeedDay(2, DayOfWeek.Tuesday),
                SeedDay(3, DayOfWeek.Wednesday),
                SeedDay(4, DayOfWeek.Thursday),
                SeedDay(5, DayOfWeek.Friday),
                SeedDay(6, DayOfWeek.Saturday),
                SeedDay(7, DayOfWeek.Sunday));
        });
    }

    /// <summary>A closure must not end before it starts.</summary>
    private static void ConfigureSpecialClosures(ModelBuilder builder)
    {
        builder.Entity<SpecialClosure>(closure =>
        {
            // Bookings look closures up by date, so index the start date.
            closure.HasIndex(c => c.StartDate);

            closure.ToTable(table => table.HasCheckConstraint(
                "CK_SpecialClosures_EndNotBeforeStart",
                "[EndDate] >= [StartDate]"));
        });
    }

    /// <summary>Unique area names, seeded with two areas for demos and WL-2 tests.</summary>
    private static void ConfigureDiningAreas(ModelBuilder builder)
    {
        builder.Entity<DiningArea>(area =>
        {
            // SQL Server's default collation is case-insensitive, so "patio" and "Patio" also clash.
            // The service checks this too, because the SQLite test database is case-sensitive.
            area.HasIndex(a => a.Name).IsUnique();

            area.HasData(
                new DiningArea { Id = MainHallId, Name = "Main Hall", Description = "Indoor, air-conditioned", IsActive = true },
                new DiningArea { Id = PatioId, Name = "Patio", Description = "Outdoor, covered", IsActive = true });
        });
    }

    /// <summary>Unique labels, capacity 1-20 and a restricted link to the area.</summary>
    private static void ConfigureDiningTables(ModelBuilder builder)
    {
        builder.Entity<DiningTable>(table =>
        {
            table.HasIndex(t => t.Label).IsUnique();

            // Restrict: an area that still has tables cannot be deleted, so no table is left without an area.
            table.HasOne(t => t.DiningArea)
                .WithMany(a => a.Tables)
                .HasForeignKey(t => t.DiningAreaId)
                .OnDelete(DeleteBehavior.Restrict);

            // The limits come from the entity's constants, so the rule is written once.
            table.ToTable(t => t.HasCheckConstraint(
                "CK_DiningTables_Capacity",
                $"[Capacity] BETWEEN {DiningTable.MinCapacity} AND {DiningTable.MaxCapacity}"));

            table.HasData(
                SeedTable(1, "T01", 2, MainHallId),
                SeedTable(2, "T02", 2, MainHallId),
                SeedTable(3, "T03", 4, MainHallId),
                SeedTable(4, "T04", 4, MainHallId),
                SeedTable(5, "T05", 6, MainHallId),
                SeedTable(6, "T06", 8, MainHallId),
                SeedTable(7, "P01", 2, PatioId),
                SeedTable(8, "P02", 4, PatioId));
        });
    }

    /// <summary>Unique category names, seeded with the five categories YJ's menu items use.</summary>
    private static void ConfigureMenuCategories(ModelBuilder builder)
    {
        builder.Entity<MenuCategory>(category =>
        {
            category.HasIndex(c => c.Name).IsUnique();

            // Display orders go up in tens so a new category can be slotted between two others.
            category.HasData(
                new MenuCategory { Id = 1, Name = "Starters", DisplayOrder = 10, IsActive = true },
                new MenuCategory { Id = 2, Name = "Grills", DisplayOrder = 20, IsActive = true },
                new MenuCategory { Id = 3, Name = "Pasta", DisplayOrder = 30, IsActive = true },
                new MenuCategory { Id = 4, Name = "Desserts", DisplayOrder = 40, IsActive = true },
                new MenuCategory { Id = 5, Name = "Beverages", DisplayOrder = 50, IsActive = true });
        });
    }

    /// <summary>Builds one seeded day with the default opening hours.</summary>
    private static OperatingHour SeedDay(int id, DayOfWeek day)
    {
        return new OperatingHour
        {
            Id = id,
            DayOfWeek = day,
            OpensAt = DefaultOpensAt,
            ClosesAt = DefaultClosesAt,
            IsClosed = false
        };
    }

    /// <summary>Builds one seeded, active table.</summary>
    private static DiningTable SeedTable(int id, string label, int capacity, int areaId)
    {
        return new DiningTable
        {
            Id = id,
            Label = label,
            Capacity = capacity,
            DiningAreaId = areaId,
            IsActive = true
        };
    }
}
