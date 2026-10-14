using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Restaurant setup home: edit the restaurant's name, address, phone and email. Owner: WL (WL-1).
/// </summary>
/// <remarks>
/// Only administrators can open this page because Program.cs protects the whole Admin area folder.
/// The page talks to <see cref="IRestaurantService"/> and never to the database directly.
/// </remarks>
public class IndexModel : PageModel
{
    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant service from dependency injection.</summary>
    /// <param name="restaurantService">The restaurant setup service.</param>
    public IndexModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>The form fields. [BindProperty] fills them from the posted form.</summary>
    [BindProperty]
    public RestaurantProfileInput Input { get; set; } = new RestaurantProfileInput();

    /// <summary>Handles GET /Admin/Restaurant: copies the saved details into the form.</summary>
    public async Task OnGetAsync()
    {
        RestaurantProfile profile = await _restaurantService.GetProfileAsync();

        Input = new RestaurantProfileInput
        {
            Name = profile.Name,
            Address = profile.Address,
            Phone = profile.Phone,
            Email = profile.Email
        };
    }

    /// <summary>Handles POST /Admin/Restaurant: validates and saves the details.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _restaurantService.UpdateProfileAsync(Input);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        // Post-Redirect-Get: a browser refresh after saving will not submit the form again.
        TempData["StatusMessage"] = "Restaurant details saved.";
        return RedirectToPage();
    }
}