using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Analytics;

/// <summary>
/// Hand-off #13 (WL-5 with DA-3): the dashboard's sales total must equal the Reports page total for the same range.
/// </summary>
/// <remarks>
/// Uses a fresh application per test, like Darren's report tests, so only these payments exist.
/// Three orders are paid inside the range (RM 20, RM 30 and RM 12.50, one of them simulated online),
/// one is paid the day after the range and one payment is Voided; only the first three may be counted.
/// </remarks>
public class SalesReconciliationTests
{
    private static readonly DateOnly From = new DateOnly(2026, 10, 5);
    private static readonly DateOnly To = new DateOnly(2026, 10, 11);

    /// <summary>Dashboard JSON and the DA-3 report agree on the total and on every day.</summary>
    [Fact]
    public async Task DashboardSales_EqualReportTotals()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await AddPaymentsAsync(factory);

        ReportDashboard report = await GetDa3ReportAsync(factory);

        HttpClient admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        HttpResponseMessage response = await admin.GetAsync(
            $"/Admin/Analytics?handler=Data&From={From:yyyy-MM-dd}&To={To:yyyy-MM-dd}");
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("salesAvailable").GetBoolean());

        // Worked out by hand: 20.00 + 30.00 = 50.00 (demo, late and Voided payments excluded).
        Assert.Equal(50m, report.PaidTotal);
        Assert.Equal(report.PaidTotal, root.GetProperty("salesTotal").GetDecimal());

        decimal dailySum = root.GetProperty("dailySales").EnumerateArray().Sum(day => day.GetDecimal());
        Assert.Equal(report.PaidTotal, dailySum);
        Assert.Equal(7, root.GetProperty("dailySales").GetArrayLength());
    }

    /// <summary>Creates the orders and payments described in the class remarks.</summary>
    private static async Task AddPaymentsAsync(TestWebApplicationFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MenuItem dish = await OrderTestData.AddDishAsync(db, "Grilled chicken", 10m);

        DateTime monday = MalaysiaTime.ToUtc(From.ToDateTime(new TimeOnly(19, 0)));
        (decimal amount, DateTime paidAtUtc, PaymentMethod method, PaymentStatus status)[] payments =
        {
            (20.00m, monday, PaymentMethod.Cash, PaymentStatus.Paid),
            (30.00m, monday.AddDays(2), PaymentMethod.CardTerminal, PaymentStatus.Paid),
            (12.50m, monday.AddDays(6), PaymentMethod.SimulatedOnline, PaymentStatus.Paid),
            (99.00m, monday.AddDays(7), PaymentMethod.Cash, PaymentStatus.Paid),      // the day after the range
            (45.00m, monday.AddDays(1), PaymentMethod.Cash, PaymentStatus.Voided)     // not a real sale
        };

        foreach ((decimal amount, DateTime paidAtUtc, PaymentMethod method, PaymentStatus status) in payments)
        {
            Order order = await OrderTestData.AddOrderAsync(db, dish.Id, 1, amount);
            db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                StaffId = order.CustomerId,
                Amount = amount,
                Method = method,
                Status = status,
                PaidAtUtc = paidAtUtc
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Darren's report for the same range, straight from his service.</summary>
    private static async Task<ReportDashboard> GetDa3ReportAsync(TestWebApplicationFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ServiceResult<ReportDashboard> result = await scope.ServiceProvider.GetRequiredService<IReportService>()
            .GetReportAsync(new ReportFilter { Period = ReportPeriod.Custom, From = From, To = To });

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        return result.Value!;
    }
}
