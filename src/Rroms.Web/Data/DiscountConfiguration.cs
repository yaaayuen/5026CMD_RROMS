using Microsoft.EntityFrameworkCore;

using Rroms.Web.Models;

namespace Rroms.Web.Data;

/// <summary>Database rules for discounts. Owner: YJ.</summary>
public static class DiscountConfiguration
{
    /// <summary>Called from ApplicationDbContext.OnModelCreating.</summary>
    /// <param name="builder">The model builder.</param>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<MenuDiscount>(discount =>
        {
            discount.Property(d => d.Name).HasMaxLength(MenuDiscount.NameMaxLength);
            discount.Property(d => d.Scope).HasConversion<string>().HasMaxLength(20);

            // "One or more categories": a join table, deleted together with either side.
            discount.HasMany(d => d.Categories)
                .WithMany()
                .UsingEntity(join => join.ToTable("MenuDiscountCategories"));

            // "Chosen dishes": a join table, deleted together with either side.
            discount.HasMany(d => d.Dishes)
                .WithMany()
                .UsingEntity(join => join.ToTable("MenuDiscountDishes"));

            discount.HasIndex(d => d.IsActive);
        });
    }
}
