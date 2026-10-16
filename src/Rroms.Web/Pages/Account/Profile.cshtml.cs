using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// "My profile" page for every signed-in user. Owner: YY (YY-4, dietary tags YY-9).
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

    //All dietary tags, for the checkboxes (YY-9)
    public IReadOnlyList<DietaryTag> DietaryTags { get; private set; } = new List<DietaryTag>();

    //IDs of the ticked "avoid" checkboxes (YY-9). Only posted by the dietary form.
    [BindProperty]
    public List<int> AvoidTagIds { get; set; } = new List<int>();

    //The consent checkbox (YY-9)
    [BindProperty]
    public bool DietaryConsent { get; set; }

    //True when the dietary preferences section should be shown (customers only)
    public bool ShowDiningPreferences
    {
        get { return Details.RoleName == RoleNames.Customer; }
    }

    //Where Cancel goes: the dashboard for the user's role, so they can leave without saving.
    public string HomePath
    {
        get { return User.GetHomePath(); }
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
        await LoadDietaryAsync(details);

        Input = new ProfileInput
        {
            FullName = details.FullName,
            PhoneNumber = details.PhoneNumber
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
        await LoadDietaryAsync(details);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _profileService.UpdateProfileAsync(
            userId, Input.FullName, Input.PhoneNumber);

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

    //Saves the dietary checkboxes. A separate form and handler, so it does not re-validate the name and phone fields. Uses Post-Redirect-Get with TempData messages.
    public async Task<IActionResult> OnPostDietaryAsync()
    {
        ServiceResult result = await _profileService.UpdateDietaryPreferencesAsync(
            User.GetUserId(), DietaryConsent, AvoidTagIds);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors[0];
        }
        else if (DietaryConsent)
        {
            TempData["StatusMessage"] = "Your dietary preferences have been saved.";
        }
        else
        {
            TempData["StatusMessage"] = "Dietary preferences cleared. We will not use them for recommendations.";
        }

        return RedirectToPage();
    }

    //Fills the dietary checkboxes with all tags and ticks the ones this customer saved (YY-9)
    private async Task LoadDietaryAsync(ProfileDetails details)
    {
        DietaryTags = await _profileService.GetDietaryTagsAsync();
        AvoidTagIds = details.AvoidedTagIds.ToList();
        DietaryConsent = details.HasDietaryConsent;
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
    }
}