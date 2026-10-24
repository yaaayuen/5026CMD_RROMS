using System.Globalization;

using Rroms.Web.Common;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>Turns a discount into words for the admin list and the customer banner. Owner: YJ.</summary>
public static class DiscountText
{
    /// <summary>What it covers, for the admin list: "Whole menu", "Pasta category", "Starters, Beverages", "3 dishes".</summary>
    /// <param name="discount">The discount (categories and dishes loaded).</param>
    public static string AppliesTo(MenuDiscount discount)
    {
        if (discount.Scope == DiscountScope.WholeMenu)
        {
            return "Whole menu";
        }

        if (discount.Scope == DiscountScope.Category)
        {
            List<string> names = discount.Categories.Select(c => c.Name).OrderBy(n => n).ToList();

            return names.Count == 1 ? names[0] + " category" : string.Join(", ", names);
        }

        return discount.Dishes.Count == 1 ? "1 dish" : discount.Dishes.Count + " dishes";
    }

    /// <summary>What it covers, inside a sentence: "the whole menu", "Starters and Beverages", "3 chosen dishes".</summary>
    /// <param name="discount">The discount.</param>
    public static string AppliesToInSentence(MenuDiscount discount)
    {
        if (discount.Scope == DiscountScope.WholeMenu)
        {
            return "the whole menu";
        }

        if (discount.Scope == DiscountScope.Category)
        {
            List<string> names = discount.Categories.Select(c => c.Name).OrderBy(n => n).ToList();

            if (names.Count <= 1)
            {
                return string.Join("", names);
            }

            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }

        return discount.Dishes.Count == 1 ? discount.Dishes.First().Name : discount.Dishes.Count + " chosen dishes";
    }

    /// <summary>The date range in Malaysia dates, or null when it has none: "1 Oct – 31 Dec".</summary>
    /// <param name="discount">The discount.</param>
    public static string? DateRange(MenuDiscount discount)
    {
        if (!discount.StartUtc.HasValue && !discount.EndUtc.HasValue)
        {
            return null;
        }

        string from = discount.StartUtc.HasValue ? FormatDate(MalaysiaTime.FromUtc(discount.StartUtc.Value)) : "now";
        string until = discount.EndUtc.HasValue ? FormatDate(MalaysiaTime.FromUtc(discount.EndUtc.Value).AddDays(-1)) : "switched off";

        if (from == until)
        {
            return from;
        }

        return from + " – " + until;
    }

    /// <summary>Days and hours: "Mon–Fri, 12:00 – 15:00" or "Every day, all day".</summary>
    /// <param name="discount">The discount.</param>
    public static string Schedule(MenuDiscount discount)
    {
        string text = DiscountDays.Describe(discount.DaysOfWeek);

        if (discount.FromMinute.HasValue && discount.ToMinute.HasValue)
        {
            text += ", " + Clock(discount.FromMinute.Value, false) + " – " + Clock(discount.ToMinute.Value, false);
        }
        else
        {
            text += ", all day";
        }

        return text;
    }

    /// <summary>The customer banner: "Weekday Lunch: 15% off the whole menu, Mon–Fri 12:00 PM–3:00 PM".</summary>
    /// <param name="discount">The discount.</param>
    public static string Banner(MenuDiscount discount)
    {
        string text = discount.Name + ": " + DiscountRules.FormatPercent(discount.Percent) + "% off "
            + AppliesToInSentence(discount) + ", " + DiscountDays.Describe(discount.DaysOfWeek);

        if (discount.FromMinute.HasValue && discount.ToMinute.HasValue)
        {
            text += " " + Clock(discount.FromMinute.Value, true) + "–" + Clock(discount.ToMinute.Value, true);
        }

        return text;
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToString("d MMM", CultureInfo.InvariantCulture);
    }

    private static string Clock(int minute, bool twelveHour)
    {
        int capped = Math.Min(minute, 1439);
        TimeOnly time = new TimeOnly(capped / 60, capped % 60);

        return time.ToString(twelveHour ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
    }
}
