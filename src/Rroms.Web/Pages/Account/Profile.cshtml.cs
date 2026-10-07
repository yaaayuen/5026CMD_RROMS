using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// "My profile" page for every signed-in user. Owner: YY (YY-4).
/// Protected by AuthorizePage("/Account/Profile") in Program.cs.
/// The form contains no user ID. The account being edited is always the one in the authentication cookie, so posting someone else's ID is impossible (no insecure direct object reference).
/// </summary>
public class ProfileModel : PageModel
{
    private readonly IProfileService _profileService;

    //Receives the profile service from dependency injection
    public ProfileModel(IProfileService profileService)
    {
        _profileService = profileService;
    }

    //Editable fields
    [BindProperty]
    public ProfileInput Input { get; set; } = new ProfileInput();

    //Read-only details shown beside the form (email, role, member since)
    public ProfileDetails Details { get; private set; } = new ProfileDetails();

    //True when the dining preferences field should be shown (customers only)
    public bool ShowDiningPreferences
    {
        get { return Details.RoleName == RoleNames.Customer; }
    }

    //Loads the signed-in user's profile into the form
    public async Task<IActionResult> OnGetAsync()
    {
        ProfileDetails? details = await _profileService.GetProfileAsync(User.GetUserId());
        if (details == null)
        {
            return NotFound();
        }

        Details = details;
        Input = new ProfileInput
        {
            FullName = details.FullName,
            PhoneNumber = details.PhoneNumber,
            DiningPreferences = details.DiningPreferences
        };

        return Page();
    }

    //Validates and saves the profile
    public async Task<IActionResult> OnPostAsync()
    {
        string userId = User.GetUserId();

        //Reload the read-only details so the page can be redisplayed if validation fails.
        ProfileDetails? details = await _profileService.GetProfileAsync(userId);
        if (details == null)
        {
            return NotFound();
        }

        Details = details;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _profileService.UpdateProfileAsync(
            userId, Input.FullName, Input.PhoneNumber, Input.DiningPreferences);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        //Post-Redirect-Get: redirecting after a successful POST stops a browser refresh resubmitting the form.
        TempData["StatusMessage"] = "Your profile has been updated.";
        return RedirectToPage();
    }

    //The profile fields a user is allowed to change
    public class ProfileInput
    {
        //Display name
        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        //Optional phone number
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(20)]
        [Display(Name = "Phone (optional)")]
        public string? PhoneNumber { get; set; }

        //Customer dining preferences
        [StringLength(500, ErrorMessage = "Dining preferences must be at most 500 characters.")]
        [Display(Name = "Dining preferences")]
        public string? DiningPreferences { get; set; }
    }
}