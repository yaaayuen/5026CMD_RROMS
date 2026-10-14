using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// The reservation status flow (WL-3), written as one transition table:
/// Pending → Confirmed → Seated → Completed, with Cancelled and NoShow as early exits.
/// </summary>
/// <remarks>
/// Every status change in <see cref="ReservationService"/> asks this table first, so the whole flow lives
/// in one place and a new rule is a one-line edit. Seated cannot go to Cancelled or NoShow: once guests are
/// at the table the kitchen may be cooking, so their orders must not be cancelled with the booking.
/// </remarks>
public static class ReservationStatusRules
{
    /// <summary>For each status, the statuses it may move to next. Completed, Cancelled and NoShow are final.</summary>
    private static readonly IReadOnlyDictionary<ReservationStatus, ReservationStatus[]> AllowedMoves =
        new Dictionary<ReservationStatus, ReservationStatus[]>
        {
            [ReservationStatus.Pending] = new[] { ReservationStatus.Confirmed, ReservationStatus.Cancelled },
            [ReservationStatus.Confirmed] = new[] { ReservationStatus.Seated, ReservationStatus.Cancelled, ReservationStatus.NoShow },
            [ReservationStatus.Seated] = new[] { ReservationStatus.Completed },
            [ReservationStatus.Completed] = Array.Empty<ReservationStatus>(),
            [ReservationStatus.Cancelled] = Array.Empty<ReservationStatus>(),
            [ReservationStatus.NoShow] = Array.Empty<ReservationStatus>()
        };

    /// <summary>True when the table allows moving from one status to another.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The requested status.</param>
    public static bool CanMove(ReservationStatus from, ReservationStatus to)
    {
        return AllowedMoves.TryGetValue(from, out ReservationStatus[]? next) && next.Contains(to);
    }

    /// <summary>The statuses a booking may move to next, e.g. to decide which buttons a staff page shows.</summary>
    /// <param name="from">The current status.</param>
    public static IReadOnlyList<ReservationStatus> NextStatuses(ReservationStatus from)
    {
        if (AllowedMoves.TryGetValue(from, out ReservationStatus[]? next))
        {
            return next;
        }

        return Array.Empty<ReservationStatus>();
    }

    /// <summary>
    /// True while the booking can still be changed or cancelled: before the guests are seated.
    /// </summary>
    /// <param name="status">The current status.</param>
    public static bool IsChangeable(ReservationStatus status)
    {
        return status == ReservationStatus.Pending || status == ReservationStatus.Confirmed;
    }
}