using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Lets a signed-in user change their password. Owner: YY (YY-4).
/// Protected by AuthorizePage("/Account/ChangePassword") in Program.cs.
/// The current password is required, so someone using an unattended signed-in browser cannot take over the account by setting a new password.
/// </summary>

public class ChangePasswordModel : PageModel 
{
    private readonly IProfileService _profileService;

    //Receives the profile servce from dependency injection
    public ChangePasswordModel(IProfileService profileService)
    {
        _profileService = profileService;
    }

    //The password fields
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new ChangePasswordInput();

    //Shows the empty form
    public void OnGet()
    {
    }

    //Validates and changed the password
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _profileService.ChangePasswordAsync(
            User.GetUserId(), Input.CurrentPassword, Input.NewPassword);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = "Your password has been changed.";
        return RedirectToPage("/Account/Profile");
    }

    //Form fields for password change
    public class ChangePasswordInput
    {
        //The user's existing password
        [Required(ErrorMessage = "Enter your current password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        //The new password
        [Required(ErrorMessage = "Choose a new password.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        //Password must match
        [Required(ErrorMessage = "Confirm your new password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The new passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}