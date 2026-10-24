using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Answers locally before considering the optional NVIDIA service.</summary>
public interface IChatbotService
{
    /// <param name="question">The visitor's question, 1 to 500 characters.</param>
    /// <param name="isCustomer">True when a customer is signed in; guests are asked to sign in for personal pages.</param>
    /// <param name="cancellationToken">Stops the optional AI call when the visitor leaves the page.</param>
    /// <param name="customerId">The signed-in customer, used only for local answers about their own orders. Never sent to the AI.</param>
    Task<ChatReply> AskAsync(string question, bool isCustomer, CancellationToken cancellationToken = default, string? customerId = null);
}
