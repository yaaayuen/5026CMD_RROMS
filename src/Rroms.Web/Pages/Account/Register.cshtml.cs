using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Customer self-registration. Owner: YY (YY-2).
/// Every account created here is a Customer. 
/// Staff accounts can only be created by an administrator (YY-5), so nobody can give themselves staff access.
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class RegisterModel : PageModel
{
    //Where a new customer lands after registering
    private const string CustomerHomePath = "/Customer";

    private readonly IAccountService _accountService;

    //Receives the account service from dependency injection
    public RegisterModel(IAccountService accountService)
    {
        _accountService = accountService;
    }

    //The data submitted by the user on the registration form
    [BindProperty]
    public RegisterInput Input { get; set; } = new RegisterInput();

    //Optional page to return to after registering. 
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    //Shows the form, or send signed-in users to their home page
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }
        return Page();
    }

    //Validated the form and creates a new customer account and signs them in
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<ApplicationUser> result = await _accountService.RegisterCustomerAsync(
            Input.FullName, Input.Email, Input.PhoneNumber, Input.Password);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return Page();
        }

        TempData["StatusMessage"] = $"Welcome to {SiteInfo.RestaurantName}, {Input.FullName.Trim()}! Your account is ready.";

        // Redirect to the return URL if it's valid, otherwise redirect to the customer home page
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return LocalRedirect(CustomerHomePath);
    }

    //Registration form fields. The password policy itself (upper, lower, digit, symbol)
    //is enforced by Identity on the server; the attributes here give early feedback.
    public class RegisterInput
    {
        /// <summary>Name shown on bookings and receipts.</summary>
        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        /// <summary>Login email; must be unique.</summary>
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Optional phone number for booking enquiries.</summary>
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(20)]
        [Display(Name = "Phone (optional)")]
        public string? PhoneNumber { get; set; }

        /// <summary>New password. Must satisfy the Identity password policy.</summary>
        [Required(ErrorMessage = "Choose a password.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Must match <see cref="Password"/>, to catch typing mistakes.</summary>
        [Required(ErrorMessage = "Confirm your password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}