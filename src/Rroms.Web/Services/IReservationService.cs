using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Table availability and booking. Owner: WL (Sprint 2, WL-2; status changes follow in WL-3).
/// </summary>
/// <remarks>
/// Pages call this interface and never use ApplicationDbContext directly.
/// The customer ID always comes from the signed-in user (User.GetUserId()), never from a form.
/// Double booking is prevented by the unique index on ReservationSlots, not only by these checks.
/// </remarks>
public interface IReservationService
{
    /// <summary>
    /// The start times a customer may choose on a date: every slot on the grid where the whole visit
    /// fits inside opening hours. Empty when the restaurant is closed, and past times are left out for today.
    /// </summary>
    /// <param name="date">A Malaysia calendar date.</param>
    Task<IReadOnlyList<TimeOnly>> GetStartTimesAsync(DateOnly date);

    /// <summary>
    /// Tables that are bookable, seat the party, and are free for the whole visit. Smallest suitable table first.
    /// Fails with a message when the request breaks a rule (closed day, past time, party too large, ...).
    /// </summary>
    /// <param name="input">Date, time and party size from the search form.</param>
    /// <param name="ignoreReservationId">
    /// When changing an existing booking, its own slots do not count as busy, so moving 19:00 to 19:30
    /// on the same table is allowed. Leave null for a new booking.
    /// </param>
    Task<ServiceResult<IReadOnlyList<AvailableTable>>> SearchAvailableTablesAsync(
        AvailabilitySearchInput input, int? ignoreReservationId = null);

    /// <summary>
    /// Books a table as Pending and notifies staff. Every rule is checked again, because the table
    /// may have been taken since the search; if two requests race, the database lets only one through.
    /// </summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    /// <param name="input">The chosen table and the search values.</param>
    /// <returns>The new reservation's ID on success.</returns>
    Task<ServiceResult<int>> CreateReservationAsync(string customerId, BookingInput input);

    /// <summary>A customer's own reservations, newest visit first.</summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    Task<IReadOnlyList<ReservationSummary>> GetCustomerReservationsAsync(string customerId);


    // ============================== WL-3: customer changes ==============================//

    /// <summary>One of the customer's own bookings, or null when it does not exist or belongs to someone else.</summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    /// <param name="reservationId">The booking to show.</param>
    Task<ReservationDetails?> GetCustomerReservationAsync(string customerId, int reservationId);

    /// <summary>
    /// The customer changes the date, time, party size or table of their own Pending or Confirmed booking,
    /// before it starts. Availability is checked again; a Confirmed booking goes back to Pending for staff to re-confirm.
    /// </summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    /// <param name="reservationId">The booking to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> ModifyByCustomerAsync(string customerId, int reservationId, BookingInput input);

    /// <summary>
    /// The customer cancels their own Pending or Confirmed booking before it starts.
    /// The table is released and the booking's unstarted orders are cancelled.
    /// </summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    /// <param name="reservationId">The booking to cancel.</param>
    Task<ServiceResult> CancelByCustomerAsync(string customerId, int reservationId);

    // ============================== WL-3: staff actions (used by Darren's daily view) ==============================//

    /// <summary>All bookings that start on a Malaysia date, earliest first, with customer names.</summary>
    /// <param name="date">A Malaysia calendar date.</param>
    Task<IReadOnlyList<ReservationDetails>> GetReservationsForDateAsync(DateOnly date);

    /// <summary>Any booking by ID, or null when it does not exist.</summary>
    /// <param name="reservationId">The booking to show.</param>
    Task<ReservationDetails?> GetReservationAsync(int reservationId);

    /// <summary>Pending → Confirmed. Notifies the customer.</summary>
    /// <param name="reservationId">The booking to confirm.</param>
    Task<ServiceResult> ConfirmAsync(int reservationId);

    /// <summary>Confirmed → Seated, when the guests arrive. From now on the kitchen may cook their orders.</summary>
    /// <param name="reservationId">The booking whose guests arrived.</param>
    Task<ServiceResult> SeatAsync(int reservationId);

    /// <summary>Seated → Completed, when the guests leave. Slots after now are released for other bookings.</summary>
    /// <param name="reservationId">The finished booking.</param>
    Task<ServiceResult> CompleteAsync(int reservationId);

    /// <summary>Staff cancel a Pending or Confirmed booking. The table is released and unstarted orders are cancelled.</summary>
    /// <param name="reservationId">The booking to cancel.</param>
    Task<ServiceResult> CancelByStaffAsync(int reservationId);

    /// <summary>
    /// Confirmed → NoShow, allowed once the start time plus a grace period has passed.
    /// The table is released and unstarted orders are cancelled.
    /// </summary>
    /// <param name="reservationId">The booking whose guests did not come.</param>
    Task<ServiceResult> MarkNoShowAsync(int reservationId);

    /// <summary>Staff change a Pending or Confirmed booking. Availability is checked again; the status is kept.</summary>
    /// <param name="reservationId">The booking to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> ModifyByStaffAsync(int reservationId, BookingInput input);


    // ============================== WL-3: orders (used by YJ-3) ==============================//

    /// <summary>
    /// Whether the customer may place an order for this booking: it must be their own and Confirmed or Seated.
    /// "Reservation not found" is returned for a missing booking and for someone else's, so other people's bookings are never revealed.
    /// </summary>
    /// <param name="customerId">The signed-in customer's ID.</param>
    /// <param name="reservationId">The booking the order is for.</param>
    Task<ServiceResult> CanOrderForReservationAsync(string customerId, int reservationId);

}