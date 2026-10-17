using Rroms.Web.Common;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Inclusive Malaysia calendar-date range. Defaults live here because model binding creates filter instances.</summary>
public class MonitoringDateFilter
{
    public DateOnly From { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
    public DateOnly To { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
}
