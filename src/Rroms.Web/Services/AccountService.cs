using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Constants;

namespace Rroms.Web.Services;

/// <summary>
/// Handles user registration, sign-in, and sign-out. This service abstracts the underlying Identity framework, providing a centralized point for authentication logic and security rules. It ensures that all account-related operations are performed consistently and securely across the application.
/// </summary>

public class AccountService : IAccountServices 
{
    //Shown for every failed login attempt, only one same message to avoid revealing whether the email exists or not.
    public const string InvalidLoginMessage = "Invalid email or password.";

    //Shown when registering a new account with an email that already exists in the system.
    public const string DuplicateEmailMessage = "An account with this email already exists.";

    //Where each role lands after sign in
    private const string AdministratorHomePath = "/Admin";
    private const string StaffHomePath = "/Staff";
    private const string CustomerHomePath = "/Customer";
    private const string DefaultHomePath = "/";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IdentityOptions _identityOptions;
    private readonly ILogger<AccountService> _logger;

    //All dependencies are injected via constructor, following the Dependency Injection pattern.
    public AccountService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<IdentityOptions> identityOptions,
        ILogger<AccountService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _identityOptions = identityOptions.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<ApplicationUser>> RegisterCustomerAsync(
        string fullName, string email, string? phoneNumber, string password)
    {
        string cleanEmail = email.Trim();

        var user = new ApplicationUser
        {
            // Email also the username, users sign in with email
            UserName = cleanEmail,
            Email = cleanEmail,
            FullName = fullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        //CreateAsync validates password policy and unique email, then stores a salted hash
        IdentityResult createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return ServiceResult<ApplicationUser>.Failure(DescribeRegistrationErrors(createResult));
        }

        //Self registration assigns the Customer role to the new user; Staff accounts are created by Admins 
        IdentityResult roleResult = await _userManager.AddToRoleAsync(user, RoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            //Never leave an account without a role. Undo the creation and report a general failure.
            _logger.LogError("Could not add new user {UserId} to the Customer role. Removing the account.", user.Id);
            await _userManager.DeleteAsync(user);
            return ServiceResult<ApplicationUser>.Failure("Registration could not be completed. Please try again.");
        }

        //isPersistent: false - the cookie ends when the browser closes, until the user chooses "remember me" at login.
        await _signInManager.SignInAsync(user, isPersistent: false);

        _logger.LogInformation("New customer account {UserId} registered.", user.Id);
        return ServiceResult<ApplicationUser>.Success(user);
    }

    public async Task<ServiceResult<string>> SignInAsync(string email, string password, bool rememberMe)
    {
        string cleanEmail = email.Trim();

        // lockoutOnFailure: true counts this attempt towards the lockout limit set in Program.cs.
        SignInResult result = await _signInManager.PasswordSignInAsync(
            cleanEmail, password, rememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            ApplicationUser? user = await _userManager.FindByEmailAsync(cleanEmail);
            if (user == null)
            {
                // Should be impossible straight after a successful sign-in, but never assume.
                await _signInManager.SignOutAsync();
                return ServiceResult<string>.Failure(InvalidLoginMessage);
            }

            string homePath = await GetHomePathAsync(user);
            _logger.LogInformation("User {UserId} signed in.", user.Id);
            return ServiceResult<string>.Success(homePath);
        }

        if (result.IsLockedOut)
        {
            // A specific message here is a deliberate trade-off (the same as Microsoft's template):
            // it helps genuine users understand why they cannot sign in.
            int minutes = (int)Math.Ceiling(_identityOptions.Lockout.DefaultLockoutTimeSpan.TotalMinutes);
            _logger.LogWarning("Locked-out sign-in attempt for {Email}.", cleanEmail);
            return ServiceResult<string>.Failure(
                $"This account is temporarily locked after too many failed attempts. Try again in {minutes} minutes.");
        }

        // Wrong password, unknown email, or deactivated account (NotAllowed): all get the same message.
        return ServiceResult<string>.Failure(InvalidLoginMessage);
    }

    public async Task SignOutAsync()
    {
        await _signInManager.SignOutAsync();
    }


    //Chooses the home page for the user's highest role.
    private async Task<string> GetHomePathAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, RoleNames.Administrator))
        {
            return AdministratorHomePath;
        }

        if (await _userManager.IsInRoleAsync(user, RoleNames.Staff))
        {
            return StaffHomePath;
        }

        if (await _userManager.IsInRoleAsync(user, RoleNames.Customer))
        {
            return CustomerHomePath;
        }

        return DefaultHomePath;
    }

    //Converts IdentityResult errors into a single user-friendly message for registration failures.
    private static string[] DescribeRegistrationErrors(IdentityResult result)
    {
        var messages = new List<string>();
        bool duplicateReported = false;

        foreach (IdentityError error in result.Errors)
        {
            bool isDuplicate = error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)
                || error.Code == nameof(IdentityErrorDescriber.DuplicateEmail);

            if (isDuplicate)
            {
                if (!duplicateReported)
                {
                    messages.Add(DuplicateEmailMessage);
                    duplicateReported = true;
                }
            }
            else
            {
                messages.Add(error.Description);
            }
        }

        return messages.ToArray();
    }
}