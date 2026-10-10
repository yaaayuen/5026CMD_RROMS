using Microsoft.EntityFrameworkCore;

using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>
/// Database rules (relationship and check constraints) for the YJ-1 menu item table.
/// </summary>
/// <remarks>
/// Same pattern as RestaurantSetupConfiguration: the rules live in their own file so
/// ApplicationDbContext, which all five members edit, needs only one line for this module.
/// Each rule is also checked in IMenuService; the database is the last line of defence
/// if a bug or a direct SQL edit bypasses the service.
/// </remarks>
public static class MenuConfiguration
{
    /// <summary>
    /// Applies every menu rule. Called once from ApplicationDbContext.OnModelCreating.
    /// </summary>
    /// <param name="builder">The model builder passed in by EF Core.</param>
    public static void Configure(ModelBuilder builder)
    {
        ConfigureMenuItems(builder);
    }

    /// <summary>Restricted link to the category, a positive price and a consistent offer.</summary>
    private static void ConfigureMenuItems(ModelBuilder builder)
    {
        builder.Entity<MenuItem>(item =>
        {
            // Restrict: categories are deactivated, never deleted, so no item is left without one.
            item.HasOne(i => i.MenuCategory)
                .WithMany()
                .HasForeignKey(i => i.MenuCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            item.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_MenuItems_PricePositive",
                    "[Price] > 0");

                // An offer is either absent, or complete: a price below the normal price and a valid date range.
                table.HasCheckConstraint(
                    "CK_MenuItems_PromoConsistent",
                    "[PromoPrice] IS NULL OR ([PromoPrice] > 0 AND [PromoPrice] < [Price] " +
                    "AND [PromoStartUtc] IS NOT NULL AND [PromoEndUtc] IS NOT NULL " +
                    "AND [PromoEndUtc] > [PromoStartUtc])");
            });
        });
    }
}