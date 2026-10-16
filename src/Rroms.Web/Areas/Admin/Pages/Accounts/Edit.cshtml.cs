using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Accounts;

/// <summary>
/// Administrator edits one account: name, phone, role and active status (YY-5)
/// The business rules (last administrator, no self-demotion) are checked in the service,
/// so they hold even if someone edits the form in their browser's developer tools.
/// </summary>

public class EditModel : PageModel
{
    private readonly IUserAdministrationService _userAdministration;

    //Receives the administration service from dependency injection
    public EditModel(IUserAdministrationService userAdministration)
    {
        _userAdministration = userAdministration;
    }

    //ID of the account being edited, taken from the URL (/Admin/Accounts/Edit/{id})
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    //The editable fields
    [BindProperty]
    public EditAccountInput Input { get; set; } = new EditAccountInput();

    //Read-only information about the account (email, join date)
    public UserSummary Account { get; private set; } = new UserSummary();

    //True when the administrator is viewing their own account (role and status are locked)
    public bool IsOwnAccount { get; private set; }

    //Loads the account into the form
    public async Task<IActionResult> OnGetAsync()
    {
        bool found = await LoadAccountAsync();
        if (!found)
        {
            return NotFound();
        }

        Input = new EditAccountInput
        {
            RoleName = Account.RoleName,
            IsActive = Account.IsActive
        };

        return Page();
    }

    //Validates and saves the changes
    public async Task<IActionResult> OnPostAsync()
    {
        bool found = await LoadAccountAsync();
        if (!found)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _userAdministration.UpdateAccessAsync(
            User.GetUserId(), Id, Input.RoleName, Input.IsActive);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = $"Access for {Account.FullName} updated.";
        return RedirectToPage("Index");
    }

    //Loads the read-only account details. Returns false if the account does not exist.
    private async Task<bool> LoadAccountAsync()
    {
        UserSummary? account = await _userAdministration.GetUserAsync(Id);
        if (account == null)
        {
            return false;
        }

        Account = account;
        IsOwnAccount = account.Id == User.GetUserId();
        return true;
    }

    //Fields an administrator can change on an account
    public class EditAccountInput
    {
        //The single role for this account
        [Required(ErrorMessage = "Choose a role.")]
        [Display(Name = "Role")]
        public string RoleName { get; set; } = string.Empty;

        //Untick to deactivate the account (the user is signed out within a minute)
        [Display(Name = "Account is active")]
        public bool IsActive { get; set; }
    }
}
