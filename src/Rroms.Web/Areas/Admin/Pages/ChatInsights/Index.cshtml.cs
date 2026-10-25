using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.ChatInsights;

/// <summary>What customers ask the chatbot, by topic, and how often the answer came from the AI or the fallback.</summary>
public class IndexModel : PageModel
{
    private readonly IKitchenInsightsService _insights;
    public IndexModel(IKitchenInsightsService insights) { _insights = insights; }
    /// <summary>7, 30 or 90 days.</summary>
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;
    public Models.ViewModels.ChatInsights Insights { get; set; } = new Models.ViewModels.ChatInsights();

    /// <summary>Share of the total as a whole percentage.</summary>
    public int Percent(int part) => Insights.Total == 0 ? 0 : (int)Math.Round(part * 100.0 / Insights.Total);

    /// <summary>Friendly names for the stored topic keys.</summary>
    public static string TopicName(string topic) => topic switch
    {
        "order-status" => "My order status", "orders" => "How to order", "booking" => "How to book",
        "booking-change" => "Change or cancel a booking", "table-check" => "Table availability", "offers" => "Offers and deals",
        "hours" => "Opening hours", "contact" => "Address and contact", "menu" => "Menu and prices", "allergy" => "Allergies",
        "feedback" => "Feedback", "account" => "Account and password", "payment" => "Payment", "greeting" => "Greeting",
        "thanks" => "Thanks", "invalid" => "Empty or too long", _ => "Other (AI or not answered)"
    };

    public async Task OnGetAsync()
    {
        Days = Days is 7 or 30 or 90 ? Days : 30;
        Insights = await _insights.GetChatInsightsAsync(Days);
    }
}
