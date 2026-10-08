using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Accounts;

/// <summary>
/// Administrator creates a new account (staff member) (YY-5)
/// </summary>

public class CreateModel : PageModel
{
    private readonly IUserAdministrationService _userAdministration;

    //Receives the administration service from dependency injection
    public CreateModel(IUserAdministrationService userAdministration)
    {
        _userAdministration = userAdministration;
    }

    //The new account's details
    [BindProperty]
    public CreateAccountInput Input { get; set; } = new CreateAccountInput();

    //Shows the empty form, with Staff pre-selected as the most common case
    public void OnGet()
    {
        Input.RoleName = RoleNames.Staff;
    }

    //Validates and creates the account
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<string> result = await _userAdministration.CreateUserAsync(
            Input.FullName, Input.Email, Input.PhoneNumber, Input.Password, Input.RoleName);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = $"{Input.RoleName} account created for {Input.FullName.Trim()}. Give them their temporary password in person and ask them to change it.";
        return RedirectToPage("Index");
    }

    //Form fields for a new account
    public class CreateAccountInput
    {
        //Display name
        [Required(ErrorMessage = "Enter the person's full name.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        //Login email
        [Required(ErrorMessage = "Enter an email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        //Optional phone number
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(20)]
        [Display(Name = "Phone (optional)")]
        public string? PhoneNumber { get; set; }

        //One of the three roles
        [Required(ErrorMessage = "Choose a role.")]
        [Display(Name = "Role")]
        public string RoleName { get; set; } = string.Empty;

        //Temporary password the administrator gives to the new user
        [Required(ErrorMessage = "Set a temporary password.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Temporary password")]
        public string Password { get; set; } = string.Empty;
    }
}
