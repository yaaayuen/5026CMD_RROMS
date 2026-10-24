namespace Rroms.Web.Services;

/// <summary>Helpers for the "days of the week" bit mask of a discount (one bit per day, Sunday = bit 0).</summary>
public static class DiscountDays
{
    /// <summary>Monday to Friday.</summary>
    public static readonly int Weekdays = Bit(DayOfWeek.Monday) | Bit(DayOfWeek.Tuesday) | Bit(DayOfWeek.Wednesday)
        | Bit(DayOfWeek.Thursday) | Bit(DayOfWeek.Friday);

    /// <summary>The bit for one day.</summary>
    /// <param name="day">The day.</param>
    public static int Bit(DayOfWeek day)
    {
        return 1 << (int)day;
    }

    /// <summary>True when the mask includes the day. A mask of 0 means every day.</summary>
    /// <param name="mask">The mask.</param>
    /// <param name="day">The day.</param>
    public static bool Includes(int mask, DayOfWeek day)
    {
        return mask == 0 || (mask & Bit(day)) != 0;
    }

    /// <summary>Short readable text, for example "Mon–Fri", "Sat, Sun" or "Every day".</summary>
    /// <param name="mask">The mask.</param>
    public static string Describe(int mask)
    {
        if (mask == 0 || mask == 127)
        {
            return "Every day";
        }

        if (mask == Weekdays)
        {
            return "Mon–Fri";
        }

        DayOfWeek[] order = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        List<string> names = new List<string>();

        foreach (DayOfWeek day in order)
        {
            if ((mask & Bit(day)) != 0)
            {
                names.Add(day.ToString().Substring(0, 3));
            }
        }

        return string.Join(", ", names);
    }
}
