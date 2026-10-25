using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Seated guests calling a staff member or asking for the bill, owned by Alex.</summary>
public interface ITableRequestService
{
    /// <summary>
    /// Records the request for the customer's own seated visit and tells staff. Asking again while the same request
    /// is still open succeeds without telling staff twice.
    /// </summary>
    Task<ServiceResult> RequestAsync(string customerId, int reservationId, TableRequestKind kind);
    /// <summary>Open requests from tables that are still seated, oldest first, with the table and customer.</summary>
    Task<List<TableRequest>> GetOpenAsync();
    /// <summary>The customer's own open requests, so the tracking page can say staff are on their way.</summary>
    Task<List<TableRequest>> GetOpenForCustomerAsync(string customerId);
    /// <summary>Staff mark a request as done.</summary>
    Task<ServiceResult> HandleAsync(int id);
}
