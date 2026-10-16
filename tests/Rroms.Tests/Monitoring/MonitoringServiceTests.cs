using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Tests.Monitoring;
/// <summary>DA-7 queries against the real WL service and an isolated SQLite database.</summary>
public class MonitoringServiceTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new TestWebApplicationFactory();
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _context;
    private readonly IMonitoringService _service;
    private static readonly DateOnly Day = new DateOnly(2026, 10, 21);
    /// <summary>Starts the application and resolves the real monitoring service.</summary>
    public MonitoringServiceTests()
    {
        _factory.CreateSecureClient().Dispose();
        _scope = _factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _service = _scope.ServiceProvider.GetRequiredService<IMonitoringService>();
    }
    /// <summary>Closes the scope and its private test database.</summary>
    public void Dispose()
    {
        _scope.Dispose();
        _factory.Dispose();
    }
    /// <summary>Midnight belongs to the selected Malaysia date; next midnight does not.</summary>
    [Fact]
    public async Task DateFilter_UsesMalaysiaHalfOpenDay()
    {
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        DateTime midnight = MalaysiaTime.ToUtc(Day.ToDateTime(TimeOnly.MinValue));
        await AddAsync(customer, midnight.AddTicks(-1));
        Reservation first = await AddAsync(customer, midnight);
        Reservation last = await AddAsync(customer, midnight.AddDays(1).AddTicks(-1));
        await AddAsync(customer, midnight.AddDays(1));
        var result = await _service.GetDailyReservationsAsync(new DailyReservationFilter { Date = Day });
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { first.Id, last.Id }, result.Value!.Select(r => r.Reservation.Id));
    }
    /// <summary>Each status is supported and combines correctly with area and date.</summary>
    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Seated)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public async Task CombinedFilters_ReturnOnlyMatchingRows(ReservationStatus status)
    {
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        DateTime noon = MalaysiaTime.ToUtc(Day.ToDateTime(new TimeOnly(12, 0)));
        Reservation match = await AddAsync(customer, noon, status, 1);
        await AddAsync(customer, noon, status, 7);
        await AddAsync(customer, noon.AddDays(1), status, 1);
        ReservationStatus other = status == ReservationStatus.Pending ? ReservationStatus.Confirmed : ReservationStatus.Pending;
        await AddAsync(customer, noon, other, 1);
        var result = await _service.GetDailyReservationsAsync(new DailyReservationFilter { Date = Day, Status = status, AreaId = 1 });
        Assert.Equal(match.Id, Assert.Single(result.Value!).Reservation.Id);
    }
    /// <summary>Missing optional filters include all statuses/areas and current preferences match each customer.</summary>
    [Fact]
    public async Task DailyRows_ShowRequestsAndCurrentPreferencesWithoutCrossingCustomers()
    {
        ApplicationUser a = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser b = await OrderTestData.AddCustomerAsync(_context);
        a.DietaryConsentAtUtc = DateTime.UtcNow;
        a.AvoidedDietaryTags.Add((await _context.DietaryTags.FindAsync(1))!);
        b.DietaryConsentAtUtc = DateTime.UtcNow;
        b.AvoidedDietaryTags.Add((await _context.DietaryTags.FindAsync(2))!);
        DateTime noon = MalaysiaTime.ToUtc(Day.ToDateTime(new TimeOnly(12, 0)));
        Reservation first = await AddAsync(a, noon, ReservationStatus.Cancelled);
        first.SpecialRequests = "Birthday window seat";
        Reservation second = await AddAsync(b, noon.AddHours(1), ReservationStatus.NoShow, 7);
        await _context.SaveChangesAsync();
        var rows = (await _service.GetDailyReservationsAsync(new DailyReservationFilter { Date = Day })).Value!;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Birthday window seat", rows[0].Reservation.SpecialRequests);
        Assert.Equal("Avoids: Animal products", rows[0].DiningPreferences);
        Assert.Equal(second.Id, rows[1].Reservation.Id);
        Assert.Equal("Avoids: Dairy", rows[1].DiningPreferences);
    }
    /// <summary>Inactive areas remain available for viewing historical bookings.</summary>
    [Fact]
    public async Task InactiveArea_RemainsFilterable()
    {
        var area = await _context.DiningAreas.FindAsync(1);
        area!.IsActive = false;
        await _context.SaveChangesAsync();
        Assert.Contains(await _service.GetReservationAreasAsync(), a => a.Id == 1);
        var result = await _service.GetDailyReservationsAsync(new DailyReservationFilter { Date = Day, AreaId = 1 });
        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!);
    }
    /// <summary>Invalid IDs, statuses and overflow-prone dates produce business validation errors.</summary>
    [Theory]
    [InlineData("area")]
    [InlineData("status")]
    [InlineData("min-date")]
    [InlineData("max-date")]
    public async Task InvalidFilters_AreRejected(string kind)
    {
        var filter = new DailyReservationFilter { Date = Day };
        if (kind == "area") { filter.AreaId = -1; }
        if (kind == "status") { filter.Status = (ReservationStatus)999; }
        if (kind == "min-date") { filter.Date = DateOnly.MinValue; }
        if (kind == "max-date") { filter.Date = DateOnly.MaxValue; }
        var result = await _service.GetDailyReservationsAsync(filter);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }
    /// <summary>History excludes other customers, future visits and ongoing visits, retaining cancelled/no-show records.</summary>
    [Fact]
    public async Task History_IsPastOnlyForSelectedCustomerAndNewestFirst()
    {
        ApplicationUser a = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser b = await OrderTestData.AddCustomerAsync(_context);
        DateTime now = DateTime.UtcNow;
        Reservation older = await AddAsync(a, now.AddDays(-3), ReservationStatus.Cancelled);
        Reservation newer = await AddAsync(a, now.AddDays(-1), ReservationStatus.NoShow);
        Reservation selected = await AddAsync(a, now.AddDays(1));
        await AddAsync(a, now.AddMinutes(-30));
        await AddAsync(b, now.AddDays(-1));
        var history = await _service.GetCustomerHistoryAsync(selected.Id);
        Assert.NotNull(history);
        Assert.Equal(new[] { newer.Id, older.Id }, history.Reservations.Select(r => r.Id));
        Assert.Equal(a.FullName, history.CustomerName);
    }
    /// <summary>A customer without past bookings has an empty history; a missing booking is not found.</summary>
    [Fact]
    public async Task History_HandlesEmptyAndMissing()
    {
        ApplicationUser a = await OrderTestData.AddCustomerAsync(_context);
        Reservation selected = await AddAsync(a, DateTime.UtcNow.AddDays(1));
        Assert.Empty((await _service.GetCustomerHistoryAsync(selected.Id))!.Reservations);
        Assert.Null(await _service.GetCustomerHistoryAsync(int.MaxValue));
    }
    /// <summary>Tags without current consent are hidden on both staff views, including stale relationships.</summary>
    [Fact]
    public async Task Preferences_WithoutConsent_AreHidden()
    {
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        customer.AvoidedDietaryTags.Add((await _context.DietaryTags.FindAsync(2))!);
        Reservation booking = await AddAsync(customer, MalaysiaTime.ToUtc(Day.ToDateTime(new TimeOnly(12, 0))));
        var rows = (await _service.GetDailyReservationsAsync(new DailyReservationFilter { Date = Day })).Value!;
        Assert.Null(Assert.Single(rows).DiningPreferences);
        Assert.Null((await _service.GetCustomerHistoryAsync(booking.Id))!.DiningPreferences);
    }
    private async Task<Reservation> AddAsync(ApplicationUser customer, DateTime start, ReservationStatus status = ReservationStatus.Confirmed, int tableId = 1)
    {
        Reservation booking = await OrderTestData.AddReservationAsync(_context, customer.Id, status);
        booking.StartUtc = start;
        booking.EndUtc = start.AddHours(2);
        booking.DiningTableId = tableId;
        await _context.SaveChangesAsync();
        return booking;
    }
}
