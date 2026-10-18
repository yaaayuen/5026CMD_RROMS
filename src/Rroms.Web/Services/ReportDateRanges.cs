using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>Resolves DA-3 presets and validates dates before UTC conversion.</summary>
public static class ReportDateRanges
{
    /// <summary>Bounds daily rows and database work to one year, including leap years.</summary>
    public const int MaxRangeDays = 366;
    private const int DaysInWeek = 7;
    /// <summary>Uses Monday-Sunday weeks, whole calendar months, or inclusive custom dates.</summary>
    public static ServiceResult<ReportRange> Resolve(ReportFilter filter)
    {
        if (!Enum.IsDefined(filter.Period))
        {
            return ServiceResult<ReportRange>.Failure("Choose a valid report period.");
        }
        DateOnly from;
        DateOnly to;
        try
        {
            switch (filter.Period)
            {
                case ReportPeriod.Daily:
                    from = filter.Anchor;
                    to = filter.Anchor;
                    break;
                case ReportPeriod.Weekly:
                    int daysSinceMonday = ((int)filter.Anchor.DayOfWeek + DaysInWeek - 1) % DaysInWeek;
                    from = filter.Anchor.AddDays(-daysSinceMonday);
                    to = from.AddDays(DaysInWeek - 1);
                    break;
                case ReportPeriod.Monthly:
                    from = new DateOnly(filter.Anchor.Year, filter.Anchor.Month, 1);
                    to = new DateOnly(filter.Anchor.Year, filter.Anchor.Month, DateTime.DaysInMonth(filter.Anchor.Year, filter.Anchor.Month));
                    break;
                default:
                    from = filter.From;
                    to = filter.To;
                    break;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return ServiceResult<ReportRange>.Failure("The selected period is outside the supported date range.");
        }
        if (from <= DateOnly.MinValue || to >= DateOnly.MaxValue || from > to)
        {
            return ServiceResult<ReportRange>.Failure("Choose an ordered range between 0001-01-02 and 9999-12-30.");
        }
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return ServiceResult<ReportRange>.Failure($"Choose at most {MaxRangeDays} days.");
        }
        return ServiceResult<ReportRange>.Success(new ReportRange { From = from, To = to });
    }
}
