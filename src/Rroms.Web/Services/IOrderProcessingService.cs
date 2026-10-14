using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Alex owns order transitions after YJ creates a Placed order.</summary>
public interface IOrderProcessingService
{
    /// <summary>Kitchen work only. Hidden pre-orders are never returned.</summary>
    Task<List<Order>> GetKitchenOrdersAsync(OrderStatus? status = null);
    /// <summary>Changes status only if the current status still matches the staff form.</summary>
    Task<ServiceResult> ChangeStatusAsync(int id, OrderStatus expected, OrderStatus next);
    /// <summary>Cancels only Placed orders for a cancelled/no-show reservation. Saves pending reservation changes too.</summary>
    Task<int> CancelUnstartedOrdersForReservationAsync(int reservationId);
    /// <summary>Returns only the signed-in customer's orders.</summary>
    Task<List<Order>> GetCustomerOrdersAsync(string customerId);
}
