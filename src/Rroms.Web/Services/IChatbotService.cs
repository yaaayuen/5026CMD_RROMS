using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Answers locally before considering the optional NVIDIA service.</summary>
public interface IChatbotService
{
    Task<ChatReply> AskAsync(string question, bool isCustomer, CancellationToken cancellationToken = default);
}
