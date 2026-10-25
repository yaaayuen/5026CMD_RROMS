using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;
using Xunit.Abstractions;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-2 double-booking proof with REAL parallel requests: many customers press "Book" for the same table and
/// time at the same moment. Each customer has their own DbContext and database connection (like separate web
/// requests) to one shared database file. Exactly one booking may win.
/// </summary>
/// <remarks>
/// WHY THIS TEST EXISTS
/// Clicking "Book" in two browsers is far too slow to hit the same millisecond, so the race never shows up by hand.
/// Here 20 tasks wait behind a "gate" and are all released together, so they really book at the same moment.
///
/// THE TWO LOCKS THAT PROTECT A TABLE
/// 1. C# check in ReservationService.CreateReservationAsync: "is any slot of this table already saved?"
///    Under a race, several requests can pass this check together, because none of them has saved yet.
/// 2. Database unique index UX_ReservationSlots_Table_SlotStart (one row per table per 30-minute slot).
///    The first save wins; every later save of the same slot is rejected by the database itself.
///    The service catches that error and shows: "Sorry, that table has just been booked..."
///
/// HOW TO READ THE OUTPUT (Test Explorer > click the test > "Open additional output for this result")
/// "WON"                     = the one booking that was saved.
/// "BLOCKED by database"     = passed the C# check and tried to save, but the unique index refused it (lock 2).
/// "REFUSED by C# check"     = the winner had already saved, so the C# check said "taken" before saving (lock 1).
/// Both kinds of loser see the same friendly message. Which lock catches more depends on timing; the test only
/// requires that exactly one booking is saved.
/// </remarks>
public sealed class ParallelBookingTests : IDisposable
{
    /// <summary>How many customers press "Book" at the same moment.</summary>
    private const int Customers = 20;

    /// <summary>Seeded table T06: the only 8-seat table, so every party of 8 wants this one table.</summary>
    private const int TableT06Id = 6;

    private static readonly TimeOnly SevenPm = new TimeOnly(19, 0);

    /// <summary>One week ahead, so the booking is always in the future whatever day the test runs.</summary>
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    /// <summary>A real database FILE (not in-memory), so 20 separate connections can share it like a real server.</summary>
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"rroms-parallel-{Guid.NewGuid():N}.db");

    /// <summary>Writes lines into the test's output, shown in Test Explorer.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Runs before each test: creates the shared database file with the restaurant seed data
    /// (opening hours, tables T01-T06, P01-P02) and 20 customer accounts.
    /// </summary>
    public ParallelBookingTests(ITestOutputHelper output)
    {
        _output = output;
        using ApplicationDbContext setup = NewContext();
        setup.Database.EnsureCreated();
        for (int i = 0; i < Customers; i++)
        {
            // A reservation needs a real customer row (foreign key to AspNetUsers).
            string id = CustomerId(i);
            setup.Users.Add(new ApplicationUser
            {
                Id = id, UserName = id + "@test.local", NormalizedUserName = (id + "@test.local").ToUpperInvariant(),
                Email = id + "@test.local", NormalizedEmail = (id + "@test.local").ToUpperInvariant(), FullName = "Customer " + i
            });
        }
        setup.SaveChanges();
    }

    /// <summary>Runs after each test: deletes the database file.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch (IOException) { }
    }

    [Fact]
    public async Task TwentyCustomersBookTheSameTableAtTheSameMoment_OnlyOneSucceeds()
    {
        // STEP 1 - The gate. Every task below waits at gate.WaitAsync() until we release it.
        using var gate = new SemaphoreSlim(0);
        Stopwatch clock = new Stopwatch();

        // STEP 2 - Prepare 20 "browsers". Each one has its OWN DbContext and connection, exactly like
        // 20 separate web requests on the server. All of them want T06 at 19:00 for 8 people.
        using var ready = new CountdownEvent(Customers);
        Task<Attempt>[] requests = Enumerable.Range(0, Customers).Select(i => Task.Factory.StartNew(async () =>
        {
            var watcher = new SaveWatcher();                       // notes whether this request reached the database save
            using ApplicationDbContext context = NewContext(watcher);
            await context.Database.OpenConnectionAsync();          // connect now, so "go" starts the booking itself
            ReservationService service = NewService(context);
            ready.Signal();                                        // "I am ready"
            await gate.WaitAsync();                                // wait for "go"
            TimeSpan pressed = clock.Elapsed;                      // when this customer pressed Book
            ServiceResult<int> result = await service.CreateReservationAsync(CustomerId(i), new BookingInput
            {
                Date = VisitDate, Time = SevenPm, PartySize = 8, DiningTableId = TableT06Id
            });
            return new Attempt(i, pressed, clock.Elapsed, result, watcher.TriedToSave);
        }, TaskCreationOptions.LongRunning).Unwrap()).ToArray();   // LongRunning: each customer gets its own thread

        // STEP 3 - Wait until all 20 are ready at the gate, then "Go": release them at the same moment.
        ready.Wait();
        clock.Start();
        gate.Release(Customers);
        Attempt[] attempts = await Task.WhenAll(requests);

        // STEP 4 - Show what happened to each customer (visible in Test Explorer's output).
        Report($"{Customers} customers book T06, {VisitDate:ddd d MMM} 19:00, party of 8, at the same moment", attempts);

        // STEP 5 - The rules the test checks.
        // (a) Exactly ONE booking succeeded.
        Assert.Single(attempts, a => a.Result.Succeeded);
        // (b) Every other customer got a friendly message - no crash, no error page.
        Assert.All(attempts.Where(a => !a.Result.Succeeded), a => Assert.True(
            a.Result.Errors.Contains(ReservationService.TableJustTakenMessage)
                || a.Result.Errors.Contains(ReservationService.TableUnavailableMessage),
            string.Join(" ", a.Result.Errors)));
        // (c) The database really holds only one booking: 1 reservation and its 4 half-hour slots
        //     (19:00, 19:30, 20:00, 20:30). No double booking was saved.
        using ApplicationDbContext check = NewContext();
        Assert.Equal(1, await check.Reservations.CountAsync());
        Assert.Equal(4, await check.ReservationSlots.CountAsync());
    }

    [Fact]
    public async Task OverlappingTimesOnTheSameTableAtTheSameMoment_OnlyOneSucceeds()
    {
        // Not the same start time, but overlapping: 19:00-21:00 and 20:00-22:00 both need
        // T06's 20:00 and 20:30 slots. The unique index protects each slot, so only one can win.
        using var gate = new SemaphoreSlim(0);
        Stopwatch clock = new Stopwatch();
        TimeOnly[] times = { new TimeOnly(19, 0), new TimeOnly(20, 0) };

        using var ready = new CountdownEvent(times.Length);
        Task<Attempt>[] requests = times.Select((time, i) => Task.Factory.StartNew(async () =>
        {
            var watcher = new SaveWatcher();
            using ApplicationDbContext context = NewContext(watcher);
            await context.Database.OpenConnectionAsync();
            ReservationService service = NewService(context);
            ready.Signal();
            await gate.WaitAsync();
            TimeSpan pressed = clock.Elapsed;
            ServiceResult<int> result = await service.CreateReservationAsync(CustomerId(i), new BookingInput
            {
                Date = VisitDate, Time = time, PartySize = 8, DiningTableId = TableT06Id
            });
            return new Attempt(i, pressed, clock.Elapsed, result, watcher.TriedToSave, time);
        }, TaskCreationOptions.LongRunning).Unwrap()).ToArray();

        ready.Wait();
        clock.Start();
        gate.Release(times.Length);
        Attempt[] attempts = await Task.WhenAll(requests);

        Report("2 customers book T06 at the same moment: 19:00-21:00 and 20:00-22:00 (they share 20:00 and 20:30)", attempts);

        Assert.Single(attempts, a => a.Result.Succeeded);
        using ApplicationDbContext check = NewContext();
        Assert.Equal(1, await check.Reservations.CountAsync());
    }

    /// <summary>One customer's attempt: who, when they pressed Book, when the answer came, and the answer.</summary>
    private sealed record Attempt(int Customer, TimeSpan Pressed, TimeSpan Answered, ServiceResult<int> Result,
        bool TriedToSave, TimeOnly? Time = null)
    {
        /// <summary>Which of the two locks decided this attempt (see the class remarks).</summary>
        public string Outcome => Result.Succeeded ? "WON - booking saved"
            : TriedToSave ? "BLOCKED by database (passed the C# check)"
            : "REFUSED by C# check (slot already saved)";
    }

    /// <summary>
    /// Watches one request's DbContext: TriedToSave becomes true when the request got past the C# check
    /// and asked the database to save the booking.
    /// </summary>
    private sealed class SaveWatcher : SaveChangesInterceptor
    {
        public bool TriedToSave { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            TriedToSave = true;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Prints a small table of the attempts and a summary into the test output.</summary>
    private void Report(string title, Attempt[] attempts)
    {
        _output.WriteLine(title);
        _output.WriteLine(new string('-', 78));
        foreach (Attempt a in attempts.OrderBy(a => a.Pressed))
        {
            string time = a.Time is TimeOnly t ? $" {t:HH:mm}" : "";
            _output.WriteLine($"Customer {a.Customer,2}{time}  pressed at {a.Pressed.TotalMilliseconds,7:F1} ms  " +
                              $"answer at {a.Answered.TotalMilliseconds,7:F1} ms  {a.Outcome}");
        }
        _output.WriteLine(new string('-', 78));
        TimeSpan spread = attempts.Max(a => a.Pressed) - attempts.Min(a => a.Pressed);
        _output.WriteLine($"All pressed Book within {spread.TotalMilliseconds:F1} ms of each other.");
        _output.WriteLine($"Won: {attempts.Count(a => a.Result.Succeeded)}   " +
                          $"Blocked by database: {attempts.Count(a => a.Outcome.StartsWith("BLOCKED"))}   " +
                          $"Refused by C# check: {attempts.Count(a => a.Outcome.StartsWith("REFUSED"))}");
        Attempt? winner = attempts.SingleOrDefault(a => a.Result.Succeeded);
        if (winner != null)
        {
            _output.WriteLine($"Saved: reservation #{winner.Result.Value} for customer {winner.Customer}. " +
                              "Every other customer saw a message; nothing else was saved.");
        }
    }

    private static string CustomerId(int i) => $"parallel-customer-{i:D2}";

    /// <summary>
    /// A NEW connection to the shared file for every request. "Default Timeout=30" makes a request wait for the
    /// file lock (like SQL Server makes a second transaction wait for the first) instead of failing at once.
    /// </summary>
    private ApplicationDbContext NewContext(params IInterceptor[] interceptors) => new ApplicationDbContext(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_file};Default Timeout=30;Pooling=False")
            .AddInterceptors(interceptors)
            .Options);

    /// <summary>Builds the real ReservationService the same way the web app does (default booking rules).</summary>
    private static ReservationService NewService(ApplicationDbContext context)
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());
        var notifications = new RecordingNotificationService();
        return new ReservationService(context, new RestaurantService(context), new ReservationTimeRules(rules), rules,
            notifications, new OrderProcessingService(context, notifications));
    }
}
