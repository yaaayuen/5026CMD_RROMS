using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
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
    /// <summary>
    /// Runs the automatic kitchen: finished countdowns become Ready to serve, drinks-only orders are ready at once,
    /// and queued orders start cooking while a slot is free. Safe to call often. Returns how many orders moved.
    /// </summary>
    Task<int> AdvanceKitchenAsync();
    /// <summary>Everything on the kitchen board, with countdowns and estimated start times for the queue.</summary>
    Task<KitchenBoard> GetKitchenBoardAsync();
    /// <summary>Staff finish a cooking order now instead of waiting for its countdown.</summary>
    Task<ServiceResult> FinishEarlyAsync(int orderId);
    /// <summary>Staff give a cooking order more time; the customer is told.</summary>
    Task<ServiceResult> AddCookingTimeAsync(int orderId, int minutes);
    Task<bool> IsPausedAsync();
    /// <summary>Pauses or resumes the kitchen. While paused, nothing new starts cooking.</summary>
    Task SetPausedAsync(bool paused);
}
