using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Tests.Reports;
/// <summary>Calendar presets and safe range boundaries.</summary>
public class ReportDateRangesTests
{
    /// <summary>Presets resolve exact inclusive calendar ranges.</summary>
    [Theory]
    [InlineData(ReportPeriod.Daily, "2026-10-18", "2026-10-18", "2026-10-18")]
    [InlineData(ReportPeriod.Weekly, "2026-10-18", "2026-10-12", "2026-10-18")]
    [InlineData(ReportPeriod.Weekly, "2026-01-01", "2025-12-29", "2026-01-04")]
    [InlineData(ReportPeriod.Monthly, "2024-02-15", "2024-02-01", "2024-02-29")]
    public void Presets(ReportPeriod period, string anchor, string from, string to)
    {
        var result = ReportDateRanges.Resolve(new ReportFilter { Period = period, Anchor = DateOnly.Parse(anchor) });
        Assert.True(result.Succeeded);
        Assert.Equal(DateOnly.Parse(from), result.Value!.From);
        Assert.Equal(DateOnly.Parse(to), result.Value.To);
    }
    /// <summary>Custom dates reject unsafe or excessive ranges.</summary>
    [Theory]
    [InlineData("2024-01-01", "2024-12-31", true)]
    [InlineData("2024-01-01", "2025-01-01", false)]
    [InlineData("2026-10-02", "2026-10-01", false)]
    [InlineData("0001-01-01", "0001-01-02", false)]
    [InlineData("9999-12-30", "9999-12-31", false)]
    public void CustomRanges(string from, string to, bool valid)
    {
        Assert.Equal(valid, ReportDateRanges.Resolve(new ReportFilter { Period = ReportPeriod.Custom, From = DateOnly.Parse(from), To = DateOnly.Parse(to) }).Succeeded);
    }
    /// <summary>Unknown enum values cannot select an implicit range.</summary>
    [Fact]
    public void InvalidPeriod()
    {
        Assert.False(ReportDateRanges.Resolve(new ReportFilter { Period = (ReportPeriod)999 }).Succeeded);
    }
}
