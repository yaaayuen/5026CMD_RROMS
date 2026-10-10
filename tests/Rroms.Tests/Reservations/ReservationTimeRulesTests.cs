using Microsoft.Extensions.Options;

using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-2: the booking time rules. No database is needed, so these are fast unit tests.
/// </summary>
public class ReservationTimeRulesTests
{
    private const int DefaultVisitMinutes = 120;
    private const int DefaultSlotMinutes = 30;

    /// <summary>A fixed evening start used by most tests: 20 Oct 2026, 19:00 Malaysia = 11:00 UTC.</summary>
    private static readonly DateTime EveningStartUtc = new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc);

    /// <summary>Builds the rules with chosen numbers, the same way appsettings.json would.</summary>
    private static ReservationTimeRules CreateRules(int visitMinutes = DefaultVisitMinutes, int slotMinutes = DefaultSlotMinutes)
    {
        ReservationRulesOptions options = new ReservationRulesOptions
        {
            VisitDurationMinutes = visitMinutes,
            SlotIntervalMinutes = slotMinutes
        };

        return new ReservationTimeRules(Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public void DefaultRules_HoldFourSlotsThirtyMinutesApart()
    {
        ReservationTimeRules rules = CreateRules();

        IReadOnlyList<DateTime> slots = rules.GetSlotStartsUtc(EveningStartUtc);

        Assert.Equal(4, slots.Count);
        Assert.Equal(EveningStartUtc, slots[0]);
        Assert.Equal(EveningStartUtc.AddMinutes(90), slots[3]);
    }

    [Theory]
    [InlineData(90, 3)]
    [InlineData(100, 4)]
    [InlineData(30, 1)]
    public void ChangingVisitLength_ChangesSlotCount(int visitMinutes, int expectedSlots)
    {
        ReservationTimeRules rules = CreateRules(visitMinutes: visitMinutes);

        IReadOnlyList<DateTime> slots = rules.GetSlotStartsUtc(EveningStartUtc);

        Assert.Equal(expectedSlots, slots.Count);
    }

    [Fact]
    public void GetEndUtc_AddsTheVisitLength()
    {
        ReservationTimeRules rules = CreateRules();

        DateTime end = rules.GetEndUtc(EveningStartUtc);

        Assert.Equal(EveningStartUtc.AddMinutes(DefaultVisitMinutes), end);
    }

    [Theory]
    [InlineData(19, 0, true)]
    [InlineData(19, 30, true)]
    [InlineData(19, 15, false)]
    [InlineData(19, 45, false)]
    public void IsOnSlotGrid_AcceptsOnlyWholeSlots(int hour, int minute, bool expected)
    {
        ReservationTimeRules rules = CreateRules();

        bool result = rules.IsOnSlotGrid(new TimeOnly(hour, minute));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Overlaps_BackToBackBookings_DoNotOverlap()
    {
        DateTime firstEnd = EveningStartUtc.AddHours(2);

        bool result = ReservationTimeRules.Overlaps(EveningStartUtc, firstEnd, firstEnd, firstEnd.AddHours(2));

        Assert.False(result);
    }

    [Fact]
    public void Overlaps_PartlySharedTime_Overlaps()
    {
        bool result = ReservationTimeRules.Overlaps(
            EveningStartUtc, EveningStartUtc.AddHours(2),
            EveningStartUtc.AddHours(1), EveningStartUtc.AddHours(3));

        Assert.True(result);
    }

    [Fact]
    public void Constructor_ZeroSlotLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateRules(slotMinutes: 0));
    }
}