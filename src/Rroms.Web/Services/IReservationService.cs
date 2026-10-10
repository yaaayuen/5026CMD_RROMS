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
    Task<ServiceResult<IReadOnlyList<AvailableTable>>> SearchAvailableTablesAsync(AvailabilitySearchInput input);

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
}