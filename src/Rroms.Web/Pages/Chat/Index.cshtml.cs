using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Constants;
using Rroms.Web.Options;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Pages.Chat;

/// <summary>Razor form endpoint with antiforgery and a per-IP request limit.</summary>
[EnableRateLimiting("AlexChat")]
public class IndexModel : PageModel
{
    private readonly IChatbotService _chatbot;
    public IndexModel(IChatbotService chatbot) { _chatbot = chatbot; }
    [BindProperty, Required, StringLength(ChatbotOptions.MaxQuestionLength)]
    public string Question { get; set; } = "";
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { return BadRequest(new { answer = "Please enter a question of 1 to 500 characters." }); }
        Response.Headers.CacheControl = "no-store";
        bool isCustomer = User.IsInRole(RoleNames.Customer);
        return new JsonResult(await _chatbot.AskAsync(Question, isCustomer, HttpContext.RequestAborted, isCustomer ? User.GetUserId() : null));
    }
}
