namespace Rroms.Web.Common;

public class MalaysiaTime
{
    /// <summary>
    /// IANA time zone ID. 
    /// </summary>
    private const string TimeZoneId = "Asia/Kuala_Lumpur";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    //Converts a UTC timestamp to Malaysia local time for display.
    public static DateTime FromUtc(DateTime utc)
    {
        DateTime asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Zone);
    }

    //Converts a Malaysia local time (for example from a booking form) to UTC for storage.
    public static DateTime ToUtc(DateTime malaysiaLocal)
    {
        DateTime unspecified = DateTime.SpecifyKind(malaysiaLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }
}