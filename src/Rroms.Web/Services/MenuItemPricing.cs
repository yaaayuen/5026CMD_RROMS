using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// The one place that decides which price a customer pays right now.
/// Menu browsing (YJ-2) and order placement (YJ-3) both call this, 
/// so the price shown and the price charged can never disagree.
/// </summary>

public static class MenuItemPricing
{
    /// <summary>
    /// Returns true when the item has a complete offer and "now" is inside its window.
    /// The window includes the start moment and excludes the end moment
    /// </summary>
    /// <param name="item">The menu item to check</param>
    /// <param name="nowUtc">The current time in UTC. Passed in so tests can control it.</param>
    public static bool IsPromoActive(MenuItem item, DateTime nowUtc)
    {
        if (item.PromoPrice == null)
        {
            return false;
        }

        if (item.PromoStartUtc == null)
        {
            return false;
        }

        if (item.PromoEndUtc == null)
        {
            return false;
        }

        bool hasStarted = nowUtc >= item.PromoStartUtc.Value;
        bool hasNotEnded = nowUtc < item.PromoEndUtc.Value;

        return hasStarted && hasNotEnded;
    }

    /// <summary>
    /// Returns the promotional price while an offer is active, 
    /// otherwise the normal price.
    /// </summary>
    /// <param name="item">The menu item to price.</param>
    /// <param name="nowUtc">The current time in UTC.</param>
    public static decimal GetCurrentPrice(MenuItem item, DateTime nowUtc)
    {
        decimal? promoPrice = item.PromoPrice;

        if (promoPrice.HasValue && IsPromoActive(item, nowUtc))
        {
            return promoPrice.Value;
        }

        return item.Price;
    }
}

