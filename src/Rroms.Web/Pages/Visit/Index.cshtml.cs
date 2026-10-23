using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Visit;

/// <summary>
/// Public "Opening hours and contact" page (WL-1): anyone, signed in or not, can see when the restaurant is open,
/// the upcoming special closures and how to contact it. It only reads what the administrator set up.
/// </summary>
public class IndexModel : PageModel
{
    /// <summary>How far ahead upcoming special closures are listed.</summary>
    public const int ClosuresDaysAhead = 90;

    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant set-up service.</summary>
    public IndexModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>Name, address, phone and e-mail.</summary>
    public RestaurantProfile Profile { get; private set; } = new RestaurantProfile();

    /// <summary>The seven days, Monday first.</summary>
    public IReadOnlyList<OperatingHour> Hours { get; private set; } = new List<OperatingHour>();

    /// <summary>Special closures that have not ended yet and start within <see cref="ClosuresDaysAhead"/> days.</summary>
    public IReadOnlyList<SpecialClosure> UpcomingClosures { get; private set; } = new List<SpecialClosure>();

    /// <summary>Today in Malaysia, to highlight today's row.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>True when today is a special closure day.</summary>
    public bool ClosedToday { get; private set; }

    /// <summary>Loads the profile, the weekly hours and the upcoming closures.</summary>
    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        Profile = await _restaurantService.GetProfileAsync();

        // DayOfWeek starts at Sunday (0); show Monday first, as people read a week.
        Hours = (await _restaurantService.GetOperatingHoursAsync())
            .OrderBy(h => ((int)h.DayOfWeek + 6) % 7)
            .ToList();

        DateOnly until = Today.AddDays(ClosuresDaysAhead);
        UpcomingClosures = (await _restaurantService.GetSpecialClosuresAsync())
            .Where(c => c.EndDate >= Today && c.StartDate <= until)
            .OrderBy(c => c.StartDate)
            .ToList();
        ClosedToday = UpcomingClosures.Any(c => c.StartDate <= Today && c.EndDate >= Today);
    }
}
