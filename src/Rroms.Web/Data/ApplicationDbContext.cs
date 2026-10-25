using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Contracts;
using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>
/// The single Entity Framework Core database context for the whole system.
/// </summary>

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    /// <summary>
    /// Receives its options (SQL Server connection string) from dependency injection in Program.cs.
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // ----- Sprint 1 (YY): accounts and notifications -----
    // Users and roles come from IdentityDbContext. 

    /// <summary>Notification inbox rows (YY-6).</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Shared dietary tags (YY-9): customers avoid them, menu items contain them.</summary>
    public DbSet<DietaryTag> DietaryTags => Set<DietaryTag>();

    // ----- Sprint 2 (WL): restaurant setup and reservations -----

    /// <summary>The restaurant's contact details (always one row).</summary>
    public DbSet<RestaurantProfile> RestaurantProfiles => Set<RestaurantProfile>();

    /// <summary>Normal opening hours, one row per day of the week.</summary>
    public DbSet<OperatingHour> OperatingHours => Set<OperatingHour>();

    /// <summary>Date ranges when the restaurant is closed, e.g. public holidays.</summary>
    public DbSet<SpecialClosure> SpecialClosures => Set<SpecialClosure>();

    /// <summary>Parts of the restaurant that contain tables.</summary>
    public DbSet<DiningArea> DiningAreas => Set<DiningArea>();

    /// <summary>Tables customers can book.</summary>
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();

    /// <summary>Menu sections; YJ's menu items reference these.</summary>
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();


    /// <summary>Table bookings made by customers (WL-2).</summary>
    public DbSet<Reservation> Reservations => Set<Reservation>();

    /// <summary>Time slots held by each reservation; their unique index prevents double booking (WL-2).</summary>
    public DbSet<ReservationSlot> ReservationSlots => Set<ReservationSlot>();

    // ----- Sprint 3 (YJ): menu and orders -----
    /// <summary>Dishes and drinks on the menu; each belongs to one MenuCategory.</summary>
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    /// <summary>Food orders; each belongs to one reservation.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>The lines of an order: which dish, how many, at what price.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>Percentage discounts set by the administrator (YJ).</summary>
    public DbSet<MenuDiscount> MenuDiscounts => Set<MenuDiscount>();

    // ----- Sprint 4 (Alex): announcements and feedback -----
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Feedback> Feedback => Set<Feedback>();
    public DbSet<BlockedWord> BlockedWords => Set<BlockedWord>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<KitchenStatus> KitchenStatuses => Set<KitchenStatus>();
    public DbSet<ChatQuestionLog> ChatQuestionLogs => Set<ChatQuestionLog>();
    public DbSet<TableRequest> TableRequests => Set<TableRequest>();
    public DbSet<DishRating> DishRatings => Set<DishRating>();

    // ----- Sprint 5 (Darren): payments -----

    /// <summary>Recorded payments and receipts (DA-1).</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Conventions applied to every entity, so no member has to remember them individually.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Money: decimal(10,2) holds up to RM 99,999,999.99 with exact cents.
        // Without this, SQL Server would use decimal(18,2) and EF would warn on every decimal.
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);

        // Store enum values as readable text ("Confirmed") instead of numbers (1).
        // This makes the data understandable in SSMS and safe if enum members are reordered.
        configurationBuilder.Properties<ReservationStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<NotificationType>().HaveConversion<string>().HaveMaxLength(30);
    }

    /// <summary>
    /// Fluent API configuration: keys, indexes and relationships that attributes cannot express.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must be called first: it configures all the Identity tables.
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            // Admin account lists filter by active status, so index it.
            user.HasIndex(u => u.IsActive);
        });

        // ----- Sprint 1 (YY): notifications -----
        builder.Entity<Notification>(notification =>
        {
            // Each notification belongs to one user. Deleting a user deletes their notifications.
            notification.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The bell badge asks "how many unread for this user?" on every page load;
            // this index answers it without scanning the table.
            notification.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });
        });

        // ----- Sprint 1 (YY): shared dietary tags and customer avoid-list (YY-9) -----
        builder.Entity<DietaryTag>(tag =>
        {
            tag.HasIndex(t => t.Name).IsUnique();

            // Fixed rows with fixed IDs, created by the migration (and by EnsureCreated in tests).
            tag.HasData(DietaryTag.Seed);
        });

        // Many-to-many: one customer avoids many tags; one tag is avoided by many customers.
        // EF Core creates the join table; naming it and its columns keeps SSMS and the ERD readable.
        builder.Entity<ApplicationUser>()
            .HasMany(user => user.AvoidedDietaryTags)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "CustomerAvoidedDietaryTags",
                right => right.HasOne<DietaryTag>().WithMany().HasForeignKey("DietaryTagId"),
                left => left.HasOne<ApplicationUser>().WithMany().HasForeignKey("UserId"));

        // Sprint 2 (WL): restaurant setup rules and seed data live in their own file.
        RestaurantSetupConfiguration.Configure(builder);
        ReservationConfiguration.Configure(builder);

        // YJ (Sprint 3): menu rules
        MenuConfiguration.Configure(builder);
        DiscountConfiguration.Configure(builder);

        // YJ (Sprint 3): order rules
        OrderConfiguration.Configure(builder);
        DiningExperienceConfiguration.Configure(builder);

        // ----- Sprint 5 (Darren): payment safeguards (DA-1) -----
        PaymentConfiguration.Configure(builder);
        builder.Entity<PaymentRefund>(refund => {
            refund.HasOne(r => r.Payment).WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
            refund.HasIndex(r => r.PaymentId).IsUnique();
            refund.HasIndex(r => r.BatchId);
            refund.Property(r => r.Amount).HasPrecision(18, 2);
            refund.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            refund.ToTable("PaymentRefunds", t => t.HasCheckConstraint("CK_PaymentRefund_Amount", "[Amount] >= 0"));
        });
        builder.Entity<PaymentRefundLine>(line => {
            line.HasOne(l => l.PaymentRefund).WithMany(r => r.Lines).HasForeignKey(l => l.PaymentRefundId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: an order item that has been refunded can never be deleted.
            line.HasOne(l => l.OrderItem).WithMany().HasForeignKey(l => l.OrderItemId).OnDelete(DeleteBehavior.Restrict);
            line.Property(l => l.Amount).HasPrecision(18, 2);
            line.ToTable("PaymentRefundLines", t => {
                t.HasCheckConstraint("CK_PaymentRefundLines_Quantity", "[Quantity] > 0");
                t.HasCheckConstraint("CK_PaymentRefundLines_Amount", "[Amount] >= 0");
            });
        });
    }
}