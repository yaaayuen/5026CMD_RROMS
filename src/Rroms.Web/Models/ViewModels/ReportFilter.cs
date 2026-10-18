using Rroms.Web.Common;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Report period and Malaysia dates; presets use Anchor, custom uses From and To.</summary>
public class ReportFilter
{
    public ReportPeriod Period { get; set; } = ReportPeriod.Daily;
    public DateOnly Anchor { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
    public DateOnly From { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
    public DateOnly To { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
}
