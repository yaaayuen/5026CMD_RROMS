using Rroms.Web.Common;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>The rules for one discount: is it running now, does it cover this dish, what does it cost? Owner: YJ.</summary>
public static class DiscountRules
{
    /// <summary>True when the discount is switched on and "now" falls inside its dates, days and hours.</summary>
    /// <param name="discount">The discount.</param>
    /// <param name="nowUtc">The current time in UTC.</param>
    public static bool IsRunning(MenuDiscount discount, DateTime nowUtc)
    {
        if (!discount.IsActive)
        {
            return false;
        }

        if (discount.StartUtc.HasValue && nowUtc < discount.StartUtc.Value)
        {
            return false;
        }

        if (discount.EndUtc.HasValue && nowUtc >= discount.EndUtc.Value)
        {
            return false;
        }

        // Days and hours are read on the Malaysian clock, because that is how the admin thinks.
        DateTime local = MalaysiaTime.FromUtc(nowUtc);

        if (!DiscountDays.Includes(discount.DaysOfWeek, local.DayOfWeek))
        {
            return false;
        }

        if (discount.FromMinute.HasValue && discount.ToMinute.HasValue)
        {
            int minute = local.Hour * 60 + local.Minute;

            if (minute < discount.FromMinute.Value || minute >= discount.ToMinute.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the discount covers the dish.</summary>
    /// <param name="discount">The discount (with its dishes loaded).</param>
    /// <param name="item">The dish.</param>
    public static bool Covers(MenuDiscount discount, MenuItem item)
    {
        return discount.Scope switch
        {
            DiscountScope.WholeMenu => true,
            DiscountScope.Category => discount.Categories.Any(category => category.Id == item.MenuCategoryId),
            DiscountScope.Dishes => discount.Dishes.Any(dish => dish.Id == item.Id),
            _ => false
        };
    }

    /// <summary>The price after taking the percent off, rounded to the nearest sen.</summary>
    /// <param name="price">The normal price.</param>
    /// <param name="percent">The percent off.</param>
    public static decimal Apply(decimal price, decimal percent)
    {
        return Math.Round(price * (100m - percent) / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Text such as "15" or "12.5" (no trailing zeros).</summary>
    /// <param name="percent">The percent.</param>
    public static string FormatPercent(decimal percent)
    {
        return percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
