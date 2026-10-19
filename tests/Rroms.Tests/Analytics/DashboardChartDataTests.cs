using Rroms.Web.Models.ViewModels;

namespace Rroms.Tests.Analytics;

/// <summary>
/// WL-5 unit tests for turning an occupancy report into chart series. No database: the report is built by hand.
/// </summary>
public class DashboardChartDataTests
{
    /// <summary>
    /// Two days and a 2-weekday × 3-hour heatmap. The hour totals must be the heatmap's column sums,
    /// and every status gets one value per day, in day order.
    /// </summary>
    [Fact]
    public void FromReport_ShapesDaysStatusesAndHourTotals()
    {
        OccupancyReport report = new OccupancyReport
        {
            From = new DateOnly(2026, 10, 5),
            To = new DateOnly(2026, 10, 6),
            UtilisationPercent = 12.5,
            DailyTrend = new List<DailyReservationCount>
            {
                new DailyReservationCount { Date = new DateOnly(2026, 10, 5), Completed = 3, Cancelled = 1, GuestsServed = 8 },
                new DailyReservationCount { Date = new DateOnly(2026, 10, 6), Pending = 2, Seated = 1, GuestsServed = 4 }
            },
            Heatmap = new OccupancyHeatmap
            {
                Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday },
                Hours = new List<int> { 18, 19, 20 },
                Cells = new[]
                {
                    new[] { 1.0, 2.0, 0.5 },
                    new[] { 0.0, 1.5, 0.5 }
                }
            }
        };

        DashboardChartData data = DashboardChartData.FromReport(report);

        Assert.Equal(new[] { "Mon 5 Oct", "Tue 6 Oct" }, data.DayLabels);
        Assert.Equal(new[] { 3, 0 }, Series(data, "Completed"));
        Assert.Equal(new[] { 0, 1 }, Series(data, "Seated"));
        Assert.Equal(new[] { 0, 2 }, Series(data, "Pending"));
        Assert.Equal(new[] { 1, 0 }, Series(data, "Cancelled"));
        Assert.Equal(new[] { 8, 4 }, data.GuestsServed);
        Assert.Equal(new[] { "18:00", "19:00", "20:00" }, data.HourLabels);
        Assert.Equal(new[] { 1.0, 3.5, 1.0 }, data.TableHoursByHour);
        Assert.Equal(12.5, data.UtilisationPercent);
        Assert.False(data.SalesAvailable);
    }

    /// <summary>The values of one status series by name.</summary>
    private static IReadOnlyList<int> Series(DashboardChartData data, string name)
    {
        return data.BookingsByStatus.Single(s => s.Name == name).Values;
    }
}
