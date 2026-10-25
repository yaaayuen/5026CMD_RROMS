using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-2 double-booking proof with REAL parallel requests: many customers press "Book" for the same table and
/// time at the same moment. Each customer has their own DbContext and database connection (like separate web
/// requests) to one shared database file. Exactly one booking may win.
/// </summary>
/// <remarks>
/// Clicking in two browsers is too slow to hit the same moment, so this test starts all requests together with a
/// gate. The seed data has T06 as the only 8-seat table, so every party of 8 competes for the same slots.
/// </remarks>
public sealed class ParallelBookingTests : IDisposable
{
    private const int Customers = 20;
    private const int TableT06Id = 6;
    private static readonly TimeOnly SevenPm = new TimeOnly(19, 0);
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"rroms-parallel-{Guid.NewGuid():N}.db");

    /// <summary>Creates the shared database file with the seed data and the customers.</summary>
    public ParallelBookingTests()
    {
        using ApplicationDbContext setup = NewContext();
        setup.Database.EnsureCreated();
        for (int i = 0; i < Customers; i++)
        {
            string id = CustomerId(i);
            setup.Users.Add(new ApplicationUser
            {
                Id = id, UserName = id + "@test.local", NormalizedUserName = (id + "@test.local").ToUpperInvariant(),
                Email = id + "@test.local", NormalizedEmail = (id + "@test.local").ToUpperInvariant(), FullName = "Customer " + i
            });
        }
        setup.SaveChanges();
    }

    /// <summary>Deletes the database file.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch (IOException) { }
    }

    [Fact]
    public async Task TwentyCustomersBookTheSameTableAtTheSameMoment_OnlyOneSucceeds()
    {
        using var gate = new SemaphoreSlim(0);
        Task<ServiceResult<int>>[] requests = Enumerable.Range(0, Customers).Select(i => Task.Run(async () =>
        {
            // Each "request" has its own context and connection, like a separate browser.
            using ApplicationDbContext context = NewContext();
            ReservationService service = NewService(context);
            await gate.WaitAsync();
            return await service.CreateReservationAsync(CustomerId(i), new BookingInput
            {
                Date = VisitDate, Time = SevenPm, PartySize = 8, DiningTableId = TableT06Id
            });
        })).ToArray();

        gate.Release(Customers); // everyone presses "Book" now
        ServiceResult<int>[] results = await Task.WhenAll(requests);

        Assert.Single(results, r => r.Succeeded);
        // Every loser gets a friendly message, never a crash.
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.True(
            r.Errors.Contains(ReservationService.TableJustTakenMessage) || r.Errors.Contains(ReservationService.TableUnavailableMessage),
            string.Join(" ", r.Errors)));

        using ApplicationDbContext check = NewContext();
        Assert.Equal(1, await check.Reservations.CountAsync());
        Assert.Equal(4, await check.ReservationSlots.CountAsync()); // 19:00, 19:30, 20:00, 20:30
    }

    [Fact]
    public async Task OverlappingTimesOnTheSameTableAtTheSameMoment_OnlyOneSucceeds()
    {
        // 19:00-21:00 and 20:00-22:00 share the 20:00 and 20:30 slots of T06.
        using var gate = new SemaphoreSlim(0);
        TimeOnly[] times = { new TimeOnly(19, 0), new TimeOnly(20, 0) };
        Task<ServiceResult<int>>[] requests = times.Select((time, i) => Task.Run(async () =>
        {
            using ApplicationDbContext context = NewContext();
            ReservationService service = NewService(context);
            await gate.WaitAsync();
            return await service.CreateReservationAsync(CustomerId(i), new BookingInput
            {
                Date = VisitDate, Time = time, PartySize = 8, DiningTableId = TableT06Id
            });
        })).ToArray();

        gate.Release(times.Length);
        ServiceResult<int>[] results = await Task.WhenAll(requests);

        Assert.Single(results, r => r.Succeeded);
        using ApplicationDbContext check = NewContext();
        Assert.Equal(1, await check.Reservations.CountAsync());
    }

    private static string CustomerId(int i) => $"parallel-customer-{i:D2}";

    /// <summary>A new connection to the shared file; waits for the file lock like SQL Server waits for a row lock.</summary>
    private ApplicationDbContext NewContext() => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_file};Default Timeout=30;Pooling=False").Options);

    private static ReservationService NewService(ApplicationDbContext context)
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());
        var notifications = new RecordingNotificationService();
        return new ReservationService(context, new RestaurantService(context), new ReservationTimeRules(rules), rules,
            notifications, new OrderProcessingService(context, notifications));
    }
}
