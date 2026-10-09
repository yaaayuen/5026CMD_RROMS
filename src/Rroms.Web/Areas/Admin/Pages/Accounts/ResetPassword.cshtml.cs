using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Accounts;

/// <summary>
/// Administrator sets a nre temporary password for a user who has forgotten theirs. (YY-5)
/// </summary>

public class ResetPasswordModel : PageModel
{
    private readonly IUserAdministrationService _userAdministration;

    //Receives the administration service from dependency injection
    public ResetPasswordModel(IUserAdministrationService userAdministration)
    {
        _userAdministration = userAdministration;
    }

    //ID of the account, from the URL
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    //The new temporary password
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new ResetPasswordInput();

    //The account being reset, for display
    public UserSummary Account { get; private set; } = new UserSummary();

    //Shows the form
    public async Task<IActionResult> OnGetAsync()
    {
        UserSummary? account = await _userAdministration.GetUserAsync(Id);
        if (account == null)
        {
            return NotFound();
        }

        Account = account;
        return Page();
    }

    //Sets the new password
    public async Task<IActionResult> OnPostAsync()
    {
        UserSummary? account = await _userAdministration.GetUserAsync(Id);
        if (account == null)
        {
            return NotFound();
        }

        Account = account;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _userAdministration.ResetPasswordAsync(Id, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = $"Password reset for {account.FullName}. Give them the new password in person.";
        return RedirectToPage("Edit", new { id = Id });
    }

    //Form fields for a password reset
    public class ResetPasswordInput
    {
        //The new temporary password
        [Required(ErrorMessage = "Enter a new password.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New temporary password")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
