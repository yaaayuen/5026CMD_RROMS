using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Answers locally before considering the optional NVIDIA service.</summary>
public interface IChatbotService
{
    /// <param name="customerId">The signed-in customer, used only for local answers about their own orders. Never sent to the AI.</param>
    Task<ChatReply> AskAsync(string question, bool isCustomer, CancellationToken cancellationToken = default, string? customerId = null);
}
