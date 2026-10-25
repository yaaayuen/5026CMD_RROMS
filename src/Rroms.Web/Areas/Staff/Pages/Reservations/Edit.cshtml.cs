using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Reservations;

/// <summary>
/// Staff edit a customer's booking (WL-3, brief: staff "confirm, modify, or cancel customer reservations").
/// The same steps as the customer's Change page: the form starts with the booking's current values, the search
/// ignores the booking's own slots, and saving calls <see cref="IReservationService.ModifyByStaffAsync"/>,
/// which checks availability again, keeps the status and notifies the customer.
/// </summary>
public class EditModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly ReservationRulesOptions _rules;

    /// <summary>Receives the booking service and the booking rules from dependency injection.</summary>
    public EditModel(IReservationService reservationService, IOptions<ReservationRulesOptions> rules)
    {
        _reservationService = reservationService;
        _rules = rules.Value;
    }

    /// <summary>New date, time and party size. From the query string on GET, from hidden fields on POST.</summary>
    [BindProperty(SupportsGet = true)]
    public AvailabilitySearchInput Search { get; set; } = new AvailabilitySearchInput();

    /// <summary>True when staff pressed "Find tables".</summary>
    [BindProperty(SupportsGet = true)]
    public bool Find { get; set; }

    /// <summary>The table chosen with the radio buttons.</summary>
    [BindProperty]
    public int DiningTableId { get; set; }

    /// <summary>The note, pre-filled with the booking's current note.</summary>
    [BindProperty]
    public string? SpecialRequests { get; set; }

    /// <summary>The booking being edited, as it is now.</summary>
    public ReservationDetails Booking { get; private set; } = new ReservationDetails();

    /// <summary>False once the booking is Seated, Completed, Cancelled or Absent.</summary>
    public bool CanChange { get; private set; }

    /// <summary>Start times on offer for the chosen date.</summary>
    public IReadOnlyList<TimeOnly> StartTimes { get; private set; } = new List<TimeOnly>();

    /// <summary>Free tables (including the booking's own), or null before a search.</summary>
    public IReadOnlyList<AvailableTable>? Results { get; private set; }

    public DateOnly MinDate { get; private set; }
    public DateOnly MaxDate { get; private set; }
    public int MinPartySize => _rules.MinPartySize;
    public int MaxPartySize => _rules.MaxPartySize;

    /// <summary>The day the booking is on now, for the "Back to the day" link.</summary>
    public DateOnly BookingDay => DateOnly.FromDateTime(MalaysiaTime.FromUtc(Booking.StartUtc));

    /// <summary>Handles GET /Staff/Reservations/Edit/{id}.</summary>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadBookingAsync(id))
        {
            return NotFound();
        }

        // First visit: start from the booking's current values, so staff only edit what changes.
        if (Search.Date == default)
        {
            DateTime startLocal = MalaysiaTime.FromUtc(Booking.StartUtc);
            Search.Date = DateOnly.FromDateTime(startLocal);
            Search.Time = TimeOnly.FromDateTime(startLocal);
            Search.PartySize = Booking.PartySize;
            Find = true; // show the current table straight away
        }

        SpecialRequests = Booking.SpecialRequests;
        await LoadChoicesAsync(id);
        return Page();
    }

    /// <summary>Handles POST: saves the new date, time, party size, table and note.</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadBookingAsync(id))
        {
            return NotFound();
        }

        ServiceResult result = await _reservationService.ModifyByStaffAsync(id, new BookingInput
        {
            Date = Search.Date,
            Time = Search.Time,
            PartySize = Search.PartySize,
            DiningTableId = DiningTableId,
            SpecialRequests = SpecialRequests
        });
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

        TempData["StatusMessage"] = $"Booking #{id} has been updated. The customer has been notified.";
        return RedirectToPage("Index", new { Date = Search.Date.ToString("yyyy-MM-dd") });
    }

    /// <summary>Loads any booking (staff see every customer's). False when it does not exist.</summary>
    private async Task<bool> LoadBookingAsync(int id)
    {
        ReservationDetails? details = await _reservationService.GetReservationAsync(id);
        if (details == null)
        {
            return false;
        }

        Booking = details;
        CanChange = ReservationStatusRules.IsChangeable(details.Status);
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
