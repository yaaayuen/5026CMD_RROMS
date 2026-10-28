using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Weekly opening hours and special closures (holidays, renovation). Owner: WL (WL-1).
/// </summary>
/// <remarks>
/// The page has three buttons, so it uses three named handlers. Each handler receives its form
/// data as a parameter instead of [BindProperty], so saving one day does not also validate the
/// empty "add closure" form.
/// </remarks>

public class HoursModel : PageModel
{
    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant service from dependency injection.</summary>
    /// <param name="restaurantService">The restaurant setup service.</param>
    public HoursModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>The seven weekday rows, Monday first.</summary>
    public IReadOnlyList<OperatingHour> Hours { get; private set; } = new List<OperatingHour>();

    /// <summary>
    /// Times offered in the opening/closing lists: every half hour of the day (12:00 AM - 11:30 PM), matching the
    /// 30-minute booking slots. A saved time that is not on the half hour is kept in the list so it is not lost.
    /// </summary>
    /// <param name="current">The time saved now for that day.</param>
    public static IReadOnlyList<TimeOnly> TimeChoices(TimeOnly current)
    {
        List<TimeOnly> times = Enumerable.Range(0, 48).Select(i => new TimeOnly(i / 2, i % 2 * 30)).ToList();
        if (!times.Contains(current))
        {
            times.Add(current);
            times.Sort();
        }
        return times;
    }

    /// <summary>All special closures, earliest first.</summary>
    public IReadOnlyList<SpecialClosure> Closures { get; private set; } = new List<SpecialClosure>();

    /// <summary>The "add closure" form, kept so typed values survive a validation error.</summary>
    public SpecialClosureInput ClosureInput { get; set; } = new SpecialClosureInput();

    /// <summary>Handles GET /Admin/Restaurant/Hours.</summary>
    public async Task OnGetAsync()
    {
        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        ClosureInput = new SpecialClosureInput { StartDate = today, EndDate = today };

        await LoadListsAsync();
    }

    /// <summary>Saves one weekday's hours (the Save button on that row).</summary>
    /// <param name="day">The posted row.</param>
    public async Task<IActionResult> OnPostSaveDayAsync(OperatingHourInput day)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = $"Enter both times for {day.DayOfWeek}.";
            return RedirectToPage();    
        }

        ServiceResult result = await _restaurantService.UpdateOperatingHourAsync(day);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = $"{day.DayOfWeek} hours saved.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }

        return RedirectToPage();
    }

    /// <summary>Adds a special closure (the Add closure form).</summary>
    /// <param name="closureInput">The posted dates and reason.</param>
    public async Task<IActionResult> OnPostAddClosureAsync(SpecialClosureInput closureInput)
    {
        ClosureInput = closureInput;

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }

        ServiceResult<int> result = await _restaurantService.AddSpecialClosureAsync(closureInput);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadListsAsync();
            return Page();
        }

        TempData["StatusMessage"] = "Closure added.";
        return RedirectToPage();
    }

    /// <summary>Removes a special closure (the Remove button on that row).</summary>
    /// <param name="id">The closure to remove.</param>
    public async Task<IActionResult> OnPostDeleteClosureAsync(int id)
    {
        ServiceResult result = await _restaurantService.DeleteSpecialClosureAsync(id);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Closure removed.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }

        return RedirectToPage();
    }

    /// <summary>Reloads both lists, needed whenever the page is shown again.</summary>
    private async Task LoadListsAsync()
    {
        Hours = await _restaurantService.GetOperatingHoursAsync();
        Closures = await _restaurantService.GetSpecialClosuresAsync();
    }
}