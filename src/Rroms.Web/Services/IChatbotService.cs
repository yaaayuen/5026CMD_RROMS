using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Answers using local rules and restaurant data.</summary>
public interface IChatbotService
{
    Task<ChatReply> AskAsync(string question, bool isCustomer, CancellationToken cancellationToken = default);
}
