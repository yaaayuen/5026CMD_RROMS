using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// The customer's own history: reservations, orders and payments. Owner: YJ (Sprint 3, YJ-5).
/// </summary>
/// <remarks>
/// Read only. Every query filters by the customer's Id inside the service, so the page can never show someone
/// else's records. Payments are read from Darren's payment table through the order they paid for; nothing in
/// the billing code is changed.
/// </remarks>
public interface ICustomerHistoryService
{
    /// <summary>The number of rows each section shows unless told otherwise.</summary>
    const int DefaultTake = 20;

    /// <summary>Gets the customer's latest reservations, orders and payments, newest first.</summary>
    /// <param name="customerId">The signed-in customer. Only this customer's records are returned.</param>
    /// <param name="take">The most rows to return in each section. Zero or less returns empty sections.</param>
    Task<CustomerHistory> GetHistoryAsync(string customerId, int take = DefaultTake);
}
