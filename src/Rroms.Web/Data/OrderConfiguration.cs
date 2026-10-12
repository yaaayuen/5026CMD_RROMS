using Microsoft.EntityFrameworkCore;

using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>
/// Database rules (relationships, indexes and check constraints) for the YJ-0 order tables.
/// </summary>
/// <remarks>
/// Same pattern as RestaurantSetupConfiguration and MenuConfiguration: 
/// the rules live in their own file so
/// ApplicationDbContext, which all five members edit, needs only one line for this module.
/// Each rule is also checked in the service layer; the database is the last line of defence
/// if a bug or a direct SQL edit bypasses the service.
/// </remarks>
public static class OrderConfiguration
{
    /// <summary>
    /// Applies every order rule. Called once from ApplicationDbContext.OnModelCreating.
    /// </summary>
    /// <param name="builder">The model builder passed in by EF Core.</param>
    public static void Configure(ModelBuilder builder)
    {
        ConfigureOrders(builder);
        ConfigureOrderItems(builder);
    }

    /// <summary>Restricted links to the reservation and customer, 
    /// lookup indexes and a non-negative total.</summary>
    private static void ConfigureOrders(ModelBuilder builder)
    {
        builder.Entity<Order>(order =>
        {
            // Restrict: reservations and users are never deleted (they change status or are deactivated),
            // so an order can never lose the booking or the person it belongs to.
            order.HasOne(o => o.Reservation)
                .WithMany()
                .HasForeignKey(o => o.ReservationId)
                .OnDelete(DeleteBehavior.Restrict);

            order.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // "My orders", newest first: one index serves both the filter and the sort.
            order.HasIndex(o => new { o.CustomerId, o.PlacedAtUtc });

            // The kitchen queue filters by status.
            order.HasIndex(o => o.Status);

            order.ToTable(table => table.HasCheckConstraint(
                "CK_Orders_TotalNotNegative",
                "[TotalAmount] >= 0"));
        });
    }

    /// <summary>Lines die with their order; a dish that has been ordered cannot be deleted; sane quantity and price.</summary>
    private static void ConfigureOrderItems(ModelBuilder builder)
    {
        builder.Entity<OrderItem>(line =>
        {
            // Cascade: a line means nothing without its order.
            line.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: order history must keep its dishes. The menu service shows a friendly message first.
            line.HasOne(i => i.MenuItem)
                .WithMany()
                .HasForeignKey(i => i.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            line.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_OrderItems_QuantityPositive",
                    "[Quantity] > 0");

                table.HasCheckConstraint(
                    "CK_OrderItems_UnitPriceNotNegative",
                    "[UnitPrice] >= 0");
            });
        });
    }
}