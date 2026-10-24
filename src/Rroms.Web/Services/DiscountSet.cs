using Microsoft.EntityFrameworkCore;

using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// A snapshot of everything pricing needs: the switched-on discounts. Load it once
/// per request and pass it to <see cref="MenuItemPricing.Quote"/>, so pricing itself needs no database. Owner: YJ.
/// </summary>
public sealed class DiscountSet
{
    /// <summary>No discounts.</summary>
    public static readonly DiscountSet Empty = new DiscountSet(new List<MenuDiscount>());

    /// <summary>Creates a set (tests build one directly).</summary>
    /// <param name="discounts">The discounts, with their <see cref="MenuDiscount.Dishes"/> filled in.</param>
    public DiscountSet(IEnumerable<MenuDiscount> discounts)
    {
        Discounts = discounts.ToList();
    }

    /// <summary>The discounts.</summary>
    public IReadOnlyList<MenuDiscount> Discounts { get; }

    /// <summary>Loads the switched-on discounts from the database.</summary>
    /// <param name="context">The database.</param>
    public static async Task<DiscountSet> LoadAsync(ApplicationDbContext context)
    {
        List<MenuDiscount> discounts = await context.MenuDiscounts
            .AsNoTracking()
            .Include(d => d.Dishes)
            .Include(d => d.Categories)
            .Where(d => d.IsActive)
            .ToListAsync();

        if (discounts.Count == 0)
        {
            return Empty;
        }

        return new DiscountSet(discounts);
    }
}
