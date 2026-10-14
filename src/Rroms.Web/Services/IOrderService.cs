using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Placing orders. Owner: YJ (Sprint 3, YJ-3). Creating orders only: every status change after
/// Placed belongs to Alex's order processing service.
/// </summary>
/// <remarks>
/// Pages call this interface and never use ApplicationDbContext directly (shared-contracts.md, section 1).
/// The rules that matter most: the server reads every price and availability from the database (the
/// request carries no price), and the order and its lines are saved together or not at all.
/// </remarks>
public interface IOrderService
{
    /// <summary>
    /// Returns the booking an order may be placed for. It must be the customer's own and Confirmed or Seated.
    /// A booking that does not exist and one that belongs to someone else give the same answer, so a customer
    /// can never find out about other people's bookings.
    /// </summary>
    /// <param name="customerId">The signed-in customer's user ID.</param>
    /// <param name="reservationId">The reservation to order for.</param>
    Task<ServiceResult<OrderableReservation>> GetOrderableReservationAsync(string customerId, int reservationId);

    /// <summary>
    /// Places an order. Checks the reservation, the quantities and every dish (it must exist, be available and be
    /// on the visible menu), prices each line from the database, saves the order with all its lines in one
    /// transaction, then notifies the customer and the staff.
    /// </summary>
    /// <param name="customerId">The signed-in customer's user ID.</param>
    /// <param name="request">The reservation, dishes and notes. It carries no prices.</param>
    /// <returns>The new order's Id on success.</returns>
    Task<ServiceResult<int>> PlaceOrderAsync(string customerId, PlaceOrderRequest request);

    /// <summary>
    /// Returns one of the customer's own orders for the confirmation page, or null when it does not exist or
    /// belongs to someone else.
    /// </summary>
    /// <param name="customerId">The signed-in customer's user ID.</param>
    /// <param name="orderId">The order to show.</param>
    Task<OrderSummary?> GetOrderForCustomerAsync(string customerId, int orderId);
}
