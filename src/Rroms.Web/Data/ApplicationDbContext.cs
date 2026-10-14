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
    // Users and roles come from IdentityDbContext. Notification table is added in YY-6.

    // ----- Sprint 2 (WL): restaurant setup and reservations -----

    // ----- Sprint 3 (YJ): menu and orders -----

    // ----- Sprint 4 (Alex): announcements and feedback -----

    // ----- Sprint 5 (Darren): payments -----

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
    }
}