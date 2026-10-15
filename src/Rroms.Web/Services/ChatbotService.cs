using System.Globalization;
using System.Text.RegularExpressions;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
namespace Rroms.Web.Services;

/// <summary>Local intent rules use live restaurant data. No external AI requests are made.</summary>
public class ChatbotService : IChatbotService
{
    private readonly IMenuService _menu;
    private readonly IRestaurantService _restaurant;
    private readonly IReservationService _reservations;
    public ChatbotService(IMenuService menu, IRestaurantService restaurant, IReservationService reservations)
    {
        _menu = menu;
        _restaurant = restaurant;
        _reservations = reservations;
    }

    public async Task<ChatReply> AskAsync(string question, bool isCustomer, CancellationToken cancellationToken = default)
    {
        question = question.Trim();
        if (question.Length == 0 || question.Length > ChatbotOptions.MaxQuestionLength)
        {
            return Reply("Please enter a question of 1 to 500 characters.");
        }
        string text = question.ToLowerInvariant();
        // Personal records remain on authenticated pages; they are never sent to the AI service.
        if (Has(text, "order", "orders", "tracking", "status", "ready"))
        {
            return CustomerPage(isCustomer, "Pre-orders reach the kitchen only after you are seated. You can check your current order and reservation status on Track my visit.", "/Customer/Tracking", "Track my visit");
        }
        if (Has(text, "book", "booking", "reservation", "reserve", "table", "availability", "available tables"))
        {
            Match request = Regex.Match(text, @"(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2})\s+for\s+(?<party>\d{1,2})\b");
            if (request.Success && DateOnly.TryParseExact(request.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && TimeOnly.TryParseExact(request.Groups["time"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                var result = await _reservations.SearchAvailableTablesAsync(new AvailabilitySearchInput
                { Date = date, Time = time, PartySize = int.Parse(request.Groups["party"].Value, CultureInfo.InvariantCulture) });
                string answer = result.Succeeded ? $"There are {result.Value!.Count} suitable tables available for that visit. Availability may change until you book."
                    : string.Join(" ", result.Errors);
                return CustomerPage(isCustomer, answer, "/Customer/Reservations", "My reservations");
            }
            return CustomerPage(isCustomer, "For a live check, ask: table yyyy-MM-dd HH:mm for 2. Or choose a date, time and party size on the booking page to check live availability. Existing bookings can be changed from My reservations.", "/Customer/Reservations", "My reservations");
        }
        if (Has(text, "feedback", "review", "rating", "rate"))
        {
            return CustomerPage(isCustomer, "You can leave one rating from 1 to 5 stars after staff complete your visit.", "/Customer/Feedback", "Dining feedback");
        }
        if (Has(text, "profile", "password", "account"))
        {
            return CustomerPage(isCustomer, "Your profile page contains your account details and links to password settings.", "/Account/Profile", "My profile");
        }
        if (Has(text, "promotion", "promotions", "campaign", "announcement", "offers"))
        {
            return Reply("Current campaigns appear on News and offers. Individual dish offers appear on the menu.", "/Announcements", "News and offers");
        }
        if (Has(text, "hours", "open", "opening", "close", "closing"))
        {
            var hours = await _restaurant.GetOperatingHoursAsync();
            var today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
            string todayText = await _restaurant.IsClosedOnAsync(today) ? "Closed today. " : "";
            string week = string.Join("; ", hours.Select(h => h.DayOfWeek + ": " +
                (h.IsClosed ? "closed" : h.OpensAt.ToString("HH:mm") + " to " + h.ClosesAt.ToString("HH:mm"))));
            return Reply(todayText + "Usual hours (Malaysia time): " + week + ". Special closures are checked on the booking page.");
        }
        if (Has(text, "address", "location", "contact", "phone", "email"))
        {
            var profile = await _restaurant.GetProfileAsync();
            return Reply(profile.Name + ": " + profile.Address + ". Phone: " + profile.Phone + ". Email: " + profile.Email);
        }
        if (Has(text, "payment", "pay", "bill", "cash", "card"))
        {
            return Reply("Staff handle billing at the restaurant. This coursework system records payments; any online payment is a labelled simulation, not a real charge.");
        }
        if (text is "hi" or "hello" or "hey" || Has(text, "help", "faq"))
        {
            return Reply("Hello! Ask me about the menu, opening hours or booking a table. I can also help you find your order status.");
        }
        var menu = await _menu.SearchMenuAsync(new MenuSearchCriteria());
        var matches = menu.Where(m => text.Contains(m.Name.ToLowerInvariant())).Take(5).ToList();
        bool menuQuestion = Has(text, "menu", "price", "cost", "dish", "food", "vegetarian", "drink", "allergy", "allergen");
        if (matches.Count > 0 || menuQuestion)
        {
            if (Has(text, "allergy", "allergen"))
            {
                return Reply("Please confirm allergies with restaurant staff before ordering. Menu descriptions alone cannot guarantee that a dish is safe for an allergy.", "/Menu", "Menu");
            }
            if (matches.Count == 0)
            {
                return Reply("The menu shows current prices, dish descriptions and availability. Use its search and filters to find a dish.", "/Menu", "Menu");
            }
            return Reply(string.Join("; ", matches.Select(m => m.Name + ": RM " + m.CurrentPrice.ToString("0.00")
                + (m.CanOrder ? " (available)" : " (unavailable)"))), "/Menu", "Menu");
        }
        return Fallback();
    }

    private static bool Has(string text, params string[] phrases)
    {
        return phrases.Any(p => Regex.IsMatch(text, @"\b" + Regex.Escape(p) + @"\b"));
    }
    private static ChatReply Reply(string answer, string? url = null, string? label = null)
    {
        return new ChatReply { Answer = answer, PageUrl = url, PageLabel = label };
    }
    private static ChatReply CustomerPage(bool customer, string answer, string url, string label)
    {
        if (!customer) { return Reply("Please sign in with a customer account to use this feature.", "/Account/Login", "Sign in"); }
        return Reply(answer, url, label);
    }
    private static ChatReply Fallback()
    {
        return Reply("I cannot answer that right now. Try asking about the menu, booking a table or opening hours, or speak with restaurant staff.");
    }
}
