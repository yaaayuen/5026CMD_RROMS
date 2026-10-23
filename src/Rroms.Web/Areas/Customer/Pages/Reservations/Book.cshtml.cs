using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Reservations;

/// <summary>
/// Customer table booking (WL-2): pick a date, time and party size, see the free tables, book one.
/// </summary>
/// <remarks>
/// Searching is a GET (it only reads, so the URL can be bookmarked and the Back button works).
/// Booking is a POST (it changes data, so it carries the antiforgery token).
/// The customer is always the signed-in user; no customer ID is ever read from the form.
/// </remarks>
public class BookModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly IRestaurantService _restaurantService;
    private readonly ReservationRulesOptions _rules;

    /// <summary>Receives the booking service, the restaurant set-up and the booking rules from dependency injection.</summary>
    /// <param name="reservationService">Availability search and booking.</param>
    /// <param name="restaurantService">Bookable tables, used to find the largest table when nothing fits.</param>
    /// <param name="rules">Party size limits and booking window, used for the form's min/max.</param>
    public BookModel(IReservationService reservationService, IRestaurantService restaurantService,
        IOptions<ReservationRulesOptions> rules)
    {
        _reservationService = reservationService;
        _restaurantService = restaurantService;
        _rules = rules.Value;
    }

    /// <summary>Date, time and party size. Filled from the query string on GET and from hidden fields on POST.</summary>
    [BindProperty(SupportsGet = true)]
    public AvailabilitySearchInput Search { get; set; } = new AvailabilitySearchInput();

    /// <summary>True when the customer pressed "Find tables"; false when the page only reloads times for a new date.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Find { get; set; }

    /// <summary>The table chosen with the radio buttons.</summary>
    [BindProperty]
    public int DiningTableId { get; set; }

    /// <summary>Optional note typed under the results.</summary>
    [BindProperty]
    public string? SpecialRequests { get; set; }

    /// <summary>Start times on offer for the chosen date (empty when closed).</summary>
    public IReadOnlyList<TimeOnly> StartTimes { get; private set; } = new List<TimeOnly>();

    /// <summary>Free tables, or null when no search has been run yet.</summary>
    public IReadOnlyList<AvailableTable>? Results { get; private set; }

    /// <summary>
    /// When nothing is free at the chosen time: other start times on the same day that still have a table
    /// for this party. Empty when the whole day is full.
    /// </summary>
    public IReadOnlyList<TimeOnly> OtherFreeTimes { get; private set; } = new List<TimeOnly>();

    /// <summary>Seats at the largest bookable table; a bigger party can never be seated online.</summary>
    public int LargestTableSeats { get; private set; }

    /// <summary>Earliest date the date picker allows (today, Malaysia).</summary>
    public DateOnly MinDate { get; private set; }

    /// <summary>Latest date the date picker allows.</summary>
    public DateOnly MaxDate { get; private set; }

    /// <summary>Smallest party size, for the form's min attribute.</summary>
    public int MinPartySize
    {
        get { return _rules.MinPartySize; }
    }

    /// <summary>Largest party size, for the form's max attribute.</summary>
    public int MaxPartySize
    {
        get { return _rules.MaxPartySize; }
    }

    /// <summary>Handles GET: shows the form, the start times for the date and, after "Find tables", the free tables.</summary>
    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    /// <summary>Handles POST: books the chosen table for the signed-in customer.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        BookingInput booking = new BookingInput
        {
            Date = Search.Date,
            Time = Search.Time,
            PartySize = Search.PartySize,
            DiningTableId = DiningTableId,
            SpecialRequests = SpecialRequests
        };

        // The service checks every rule again, including that the table is still free.
        ServiceResult<int> result = await _reservationService.CreateReservationAsync(User.GetUserId(), booking);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            // Show a fresh list, so a table someone else just took disappears.
            Find = true;
            await LoadAsync();
            return Page();
        }

        TempData["StatusMessage"] = "Your table is booked and confirmed. You can pre-order food from the booking's details.";
        return RedirectToPage("Index");
    }

    /// <summary>
    /// Nothing is free at the chosen time: find the largest table and the other times that day that still
    /// have a table for this party, so the page can suggest them instead of a dead end.
    /// </summary>
    private async Task FindAlternativesAsync()
    {
        var tables = await _restaurantService.GetBookableTablesAsync();
        LargestTableSeats = tables.Count == 0 ? 0 : tables.Max(t => t.Capacity);
        if (Search.PartySize > LargestTableSeats)
        {
            return;
        }

        List<TimeOnly> free = new List<TimeOnly>();
        foreach (TimeOnly time in StartTimes.Where(t => t != Search.Time))
        {
            AvailabilitySearchInput other = new AvailabilitySearchInput
            {
                Date = Search.Date,
                Time = time,
                PartySize = Search.PartySize
            };

            // Times already in the past simply fail the search and are skipped.
            ServiceResult<IReadOnlyList<AvailableTable>> found = await _reservationService.SearchAvailableTablesAsync(other);
            if (found.Succeeded && found.Value!.Count > 0)
            {
                free.Add(time);
            }
        }

        OtherFreeTimes = free;
    }

    /// <summary>Fills the date limits, the start times and, when asked, the search results.</summary>
    private async Task LoadAsync()
    {
        MinDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        MaxDate = MinDate.AddDays(_rules.MaxDaysAhead);

        // First visit: no date in the URL yet, so start with today.
        if (Search.Date == default)
        {
            Search.Date = MinDate;
        }

        StartTimes = await _reservationService.GetStartTimesAsync(Search.Date);

        if (!Find)
        {
            return;
        }

        ServiceResult<IReadOnlyList<AvailableTable>> result = await _reservationService.SearchAvailableTablesAsync(Search);
        if (result.Succeeded)
        {
            Results = result.Value;
            if (Results!.Count == 0)
            {
                await FindAlternativesAsync();
            }
        }
        else
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }
    }
}