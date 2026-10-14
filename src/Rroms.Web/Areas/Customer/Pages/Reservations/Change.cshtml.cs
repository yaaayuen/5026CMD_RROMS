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
/// Change an existing booking (WL-3): same steps as Book, but the form starts with the booking's
/// current values, the search ignores the booking's own slots, and saving changes it instead of adding one.
/// </summary>
public class ChangeModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly ReservationRulesOptions _rules;

    /// <summary>Receives the booking service and the booking rules from dependency injection.</summary>
    /// <param name="reservationService">Availability search and booking changes.</param>
    /// <param name="rules">Party size limits and booking window, used for the form's min/max.</param>
    public ChangeModel(IReservationService reservationService, IOptions<ReservationRulesOptions> rules)
    {
        _reservationService = reservationService;
        _rules = rules.Value;
    }

    /// <summary>New date, time and party size. From the query string on GET, from hidden fields on POST.</summary>
    [BindProperty(SupportsGet = true)]
    public AvailabilitySearchInput Search { get; set; } = new AvailabilitySearchInput();

    /// <summary>True when the customer pressed "Find tables".</summary>
    [BindProperty(SupportsGet = true)]
    public bool Find { get; set; }

    /// <summary>The table chosen with the radio buttons.</summary>
    [BindProperty]
    public int DiningTableId { get; set; }

    /// <summary>The note, pre-filled with the booking's current note.</summary>
    [BindProperty]
    public string? SpecialRequests { get; set; }

    /// <summary>The booking being changed, as it is now.</summary>
    public ReservationDetails Booking { get; private set; } = new ReservationDetails();

    /// <summary>False when the booking can no longer be changed online.</summary>
    public bool CanChange { get; private set; }

    /// <summary>Start times on offer for the chosen date.</summary>
    public IReadOnlyList<TimeOnly> StartTimes { get; private set; } = new List<TimeOnly>();

    /// <summary>Free tables (including the booking's own), or null before a search.</summary>
    public IReadOnlyList<AvailableTable>? Results { get; private set; }

    /// <summary>Earliest date the date picker allows.</summary>
    public DateOnly MinDate { get; private set; }

    /// <summary>Latest date the date picker allows.</summary>
    public DateOnly MaxDate { get; private set; }

    /// <summary>Smallest party size.</summary>
    public int MinPartySize
    {
        get { return _rules.MinPartySize; }
    }

    /// <summary>Largest party size.</summary>
    public int MaxPartySize
    {
        get { return _rules.MaxPartySize; }
    }

    /// <summary>Handles GET /Customer/Reservations/Change/{id}.</summary>
    /// <param name="id">The booking to change.</param>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadBookingAsync(id))
        {
            return NotFound();
        }

        // First visit: start from the booking's current values, so the customer only edits what changes.
        if (Search.Date == default)
        {
            DateTime startLocal = MalaysiaTime.FromUtc(Booking.StartUtc);
            Search.Date = DateOnly.FromDateTime(startLocal);
            Search.Time = TimeOnly.FromDateTime(startLocal);
            Search.PartySize = Booking.PartySize;
        }

        SpecialRequests = Booking.SpecialRequests;
        await LoadChoicesAsync(id);
        return Page();
    }

    /// <summary>Handles POST: saves the new date, time, party size, table and note.</summary>
    /// <param name="id">The booking to change.</param>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadBookingAsync(id))
        {
            return NotFound();
        }

        BookingInput input = new BookingInput
        {
            Date = Search.Date,
            Time = Search.Time,
            PartySize = Search.PartySize,
            DiningTableId = DiningTableId,
            SpecialRequests = SpecialRequests
        };

        ServiceResult result = await _reservationService.ModifyByCustomerAsync(User.GetUserId(), id, input);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            Find = true;
            await LoadChoicesAsync(id);
            return Page();
        }

        TempData["StatusMessage"] = "Your booking has been changed. Our staff will confirm it again soon.";
        return RedirectToPage("Details", new { id });
    }

    /// <summary>Loads the booking for the signed-in customer. False when it is missing or someone else's.</summary>
    private async Task<bool> LoadBookingAsync(int id)
    {
        ReservationDetails? details = await _reservationService.GetCustomerReservationAsync(User.GetUserId(), id);
        if (details == null)
        {
            return false;
        }

        Booking = details;
        CanChange = ReservationStatusRules.IsChangeable(details.Status) && details.StartUtc > DateTime.UtcNow;
        return true;
    }

    /// <summary>Fills the date limits, start times and, after "Find tables", the free tables.</summary>
    private async Task LoadChoicesAsync(int id)
    {
        MinDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        MaxDate = MinDate.AddDays(_rules.MaxDaysAhead);
        StartTimes = await _reservationService.GetStartTimesAsync(Search.Date);

        if (!Find)
        {
            return;
        }

        // Pass the booking's ID so its own table and time still show as free.
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _reservationService.SearchAvailableTablesAsync(Search, id);
        if (result.Succeeded)
        {
            Results = result.Value;
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