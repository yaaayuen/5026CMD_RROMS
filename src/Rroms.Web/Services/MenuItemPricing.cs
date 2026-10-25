using Rroms.Web.Common;
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

    /// <summary>
    /// Works out the price right now from the dish's own Offer AND the administrator's discounts.
    /// The customer always pays the CHEAPEST price; offers never stack. Owner: YJ.
    /// </summary>
    /// <param name="item">The dish.</param>
    /// <param name="nowUtc">The current time in UTC.</param>
    /// <param name="discounts">The loaded discounts.</param>
    public static PriceQuote Quote(MenuItem item, DateTime nowUtc, DiscountSet discounts)
    {
        decimal best = item.Price;
        bool isOffer = false;
        string? badge = null;
        DateTime? endsAtUtc = null;

        // The dish's own Offer first, exactly as before.
        if (item.PromoPrice.HasValue && IsPromoActive(item, nowUtc))
        {
            best = item.PromoPrice.Value;
            isOffer = true;
            badge = "Offer";
            endsAtUtc = item.PromoEndUtc;
        }

        // The cheapest discount that is running now and covers this dish.
        MenuDiscount? cheapest = null;
        decimal cheapestPrice = item.Price;

        foreach (MenuDiscount discount in discounts.Discounts)
        {
            if (!DiscountRules.IsRunning(discount, nowUtc) || !DiscountRules.Covers(discount, item))
            {
                continue;
            }

            decimal discounted = DiscountRules.Apply(item.Price, discount.Percent);

            if (cheapest == null || discounted < cheapestPrice)
            {
                cheapest = discount;
                cheapestPrice = discounted;
            }
        }

        string? label = null;
        string? note = null;

        if (cheapest != null && cheapestPrice < best)
        {
            string percent = DiscountRules.FormatPercent(cheapest.Percent);

            best = cheapestPrice;
            isOffer = true;
            label = cheapest.Name + " · " + percent + "% off";
            badge = percent + "% off";
            endsAtUtc = null;
            note = cheapest.Name;

            if (cheapest.EndUtc.HasValue)
            {
                DateTime lastDay = MalaysiaTime.FromUtc(cheapest.EndUtc.Value).AddDays(-1);
                note += " until " + lastDay.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else if (cheapest != null && isOffer && cheapestPrice > best)
        {
            note = "Own offer is cheaper than the " + cheapest.Name + " promotion (RM "
                + cheapestPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + "), so it wins.";
        }

        return new PriceQuote
        {
            NormalPrice = item.Price,
            Price = best,
            IsOffer = isOffer,
            Label = label,
            Badge = badge,
            Note = note,
            EndsAtUtc = endsAtUtc
        };
    }
}
