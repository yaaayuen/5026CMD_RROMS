using Rroms.Web.Contracts;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-3: the reservation status transition table. Pure logic, so no database is needed.
/// </summary>
public class ReservationStatusRulesTests
{
    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Seated)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Seated, ReservationStatus.Completed)]
    public void CanMove_AllowedStep_IsTrue(ReservationStatus from, ReservationStatus to)
    {
        Assert.True(ReservationStatusRules.CanMove(from, to));
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Seated)]
    [InlineData(ReservationStatus.Pending, ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Seated, ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Seated, ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Completed, ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Cancelled, ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Confirmed)]
    public void CanMove_StepNotInTable_IsFalse(ReservationStatus from, ReservationStatus to)
    {
        Assert.False(ReservationStatusRules.CanMove(from, to));
    }

    [Theory]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public void NextStatuses_FinalStatus_HasNoNextStep(ReservationStatus status)
    {
        Assert.Empty(ReservationStatusRules.NextStatuses(status));
    }

    [Fact]
    public void EveryStatus_HasARowInTheTable()
    {
        // If someone adds a new value to ReservationStatus, this test fails until the table decides where it may go.
        foreach (ReservationStatus status in Enum.GetValues<ReservationStatus>())
        {
            bool isFinal = status == ReservationStatus.Completed
                || status == ReservationStatus.Cancelled
                || status == ReservationStatus.NoShow;
            Assert.Equal(isFinal, ReservationStatusRules.NextStatuses(status).Count == 0);
        }
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, true)]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Seated, false)]
    [InlineData(ReservationStatus.Completed, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public void IsChangeable_OnlyBeforeGuestsAreSeated(ReservationStatus status, bool expected)
    {
        Assert.Equal(expected, ReservationStatusRules.IsChangeable(status));
    }
}