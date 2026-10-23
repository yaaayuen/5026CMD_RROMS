using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
namespace Rroms.Web.Services;

/// <summary>Local intent rules use live restaurant data. AI is optional and cannot choose navigation URLs.</summary>
public class ChatbotService : IChatbotService
{
    private readonly IMenuService _menu;
    private readonly IRestaurantService _restaurant;
    private readonly IReservationService _reservations;
    private readonly HttpClient _http;
    private readonly ChatbotOptions _options;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(IMenuService menu, IRestaurantService restaurant, IReservationService reservations, HttpClient http,
        IOptions<ChatbotOptions> options, ILogger<ChatbotService> logger)
    {
        _menu = menu;
        _restaurant = restaurant;
        _reservations = reservations;
        _http = http;
        _options = options.Value;
        _logger = logger;
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
            return CustomerPage(isCustomer, "Pre-orders reach the kitchen only after you are seated. You can check your current order and reservation status on Track my visit.", "/Customer/Tracking", "Track my visit",
                "Open My reservations and choose a confirmed booking.",
                "Press Order food, add the dishes you want, then press Place order.",
                "The kitchen starts cooking once staff seat you. Follow each step on Track my visit.");
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
            if (Has(text, "cancel", "change", "edit", "move"))
            {
                return CustomerPage(isCustomer, "You can change or cancel a pending or confirmed booking before your visit starts.", "/Customer/Reservations", "My reservations",
                    "Open My reservations and choose the booking.",
                    "Press Change booking to pick a new date, time or party size, or press Cancel booking.",
                    "A changed booking goes back to our staff to confirm again.");
            }
            return CustomerPage(isCustomer, "Here is how to book a table. For a quick live check you can also ask: table yyyy-MM-dd HH:mm for 2.", "/Customer/Reservations", "My reservations",
                "Open My reservations and press Book a table.",
                "Choose a date, a time and the number of guests, then press Find tables.",
                "Pick one of the free tables and add any special requests, such as a birthday or a high chair.",
                "Press Book. Your booking stays Pending until staff confirm it, and you get a notification when they do.");
        }
        if (Has(text, "feedback", "review", "rating", "rate"))
        {
            return CustomerPage(isCustomer, "You can leave one rating from 1 to 5 stars after staff complete your visit.", "/Customer/Feedback", "Dining feedback",
                "Open Dining feedback after your visit is marked as completed.",
                "Choose the visit, pick a rating from 1 to 5 stars and add a comment if you like.",
                "Press Submit feedback.");
        }
        if (Has(text, "profile", "password", "account"))
        {
            return CustomerPage(isCustomer, "Your profile page contains your account details and links to password settings.", "/Account/Profile", "My profile",
                "Open My profile from your email address at the top of the page.",
                "Update your details there, or press Change password to choose a new password.");
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
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) { return Fallback(); }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 15)));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://integrate.api.nvidia.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = JsonContent.Create(new
            {
                model = _options.Model, temperature = 0.2, max_tokens = 180, stream = false,
                messages = new[]
                {
                    new { role = "system", content = "You are the Three Grills restaurant assistant. Give a brief general dining answer. You have no access to bookings, orders, prices, policies or personal data. Never invent restaurant facts or claim you changed a booking. For restaurant-specific information ask the user to use the menu or booking page. Do not supply URLs. Do not give allergy safety guarantees." },
                    new { role = "user", content = question }
                }
            });
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) { return Fallback(); }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            string? answer = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(answer)) { return Fallback(); }
            return new ChatReply { Answer = answer.Length > 2000 ? answer[..2000] : answer, Source = "AI" };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            // Do not log the question, key or response body.
            _logger.LogInformation("Chatbot fallback used: {ErrorType}", ex.GetType().Name);
            return Fallback();
        }
    }

    private static bool Has(string text, params string[] phrases)
    {
        return phrases.Any(p => Regex.IsMatch(text, @"\b" + Regex.Escape(p) + @"\b"));
    }
    private static ChatReply Reply(string answer, string? url = null, string? label = null)
    {
        return new ChatReply { Answer = answer, PageUrl = url, PageLabel = label };
    }
    private static ChatReply CustomerPage(bool customer, string answer, string url, string label, params string[] steps)
    {
        if (!customer)
        {
            // Guests still see how it works, with signing in as the first step.
            var reply = Reply("Please sign in with a customer account to use this feature.", "/Account/Login", "Sign in");
            reply.Steps = steps.Length == 0 ? steps : steps.Prepend("Sign in, or create a customer account if you are new.").ToArray();
            return reply;
        }
        var answerWithSteps = Reply(answer, url, label);
        answerWithSteps.Steps = steps;
        return answerWithSteps;
    }
    private static ChatReply Fallback()
    {
        return Reply("I cannot answer that right now. Try asking about the menu, booking a table or opening hours, or speak with restaurant staff.");
    }
}
