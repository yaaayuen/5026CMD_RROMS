using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Completed visit feedback, owned by Alex.</summary>
public interface IFeedbackService
{
    Task<List<Reservation>> GetEligibleAsync(string customerId);
    Task<ServiceResult> SubmitAsync(string customerId, int reservationId, int rating, string? comment);
    Task<List<Feedback>> GetAsync(int? rating, DateTime? fromUtc, DateTime? untilUtc);
}
