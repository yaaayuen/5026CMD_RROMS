using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Handles user registration, sign-in, and sign-out. This service abstracts the underlying Identity framework, providing a centralized point for authentication logic and security rules. It ensures that all account-related operations are performed consistently and securely across the application.
/// </summary>

public class AccountService : IAccountService 
{
    //Shown for every failed login attempt, only one same message to avoid revealing whether the email exists or not.
    public const string InvalidLoginMessage = "Invalid email or password.";

    //Shown only when a suspended user enters the correct password (YY-8).
    public const string SuspendedMessage = "Your account has been suspended. Please contact the restaurant for help.";

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
    private readonly INotificationService _notificationService;
    private readonly IAccountSecurityService _accountSecurity;
    private readonly ILogger<AccountService> _logger;

    //All dependencies are injected via constructor, following the Dependency Injection pattern.
    public AccountService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<IdentityOptions> identityOptions,
        INotificationService notificationService,
        IAccountSecurityService accountSecurity,
        ILogger<AccountService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _identityOptions = identityOptions.Value;
        _notificationService = notificationService;
        _accountSecurity = accountSecurity;
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

        // Welcome message (YY-6): appears in the new customer's inbox and is emailed to them.
        await _notificationService.NotifyUserAsync(
            user.Id,
            NotificationType.General,
            $"Welcome to {SiteInfo.RestaurantName}",
            "Your account is ready. You can now book a table, order from your seat and follow your food live.",
            "/Customer");

        _logger.LogInformation("New customer account {UserId} registered.", user.Id);
        return ServiceResult<ApplicationUser>.Success(user);
    }

    public async Task<ServiceResult<SignInOutcome>> SignInAsync(string email, string password, bool rememberMe)
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
                return ServiceResult<SignInOutcome>.Failure(InvalidLoginMessage);
            }

            string homePath = await GetHomePathAsync(user);
            _logger.LogInformation("User {UserId} signed in.", user.Id);
            return ServiceResult<SignInOutcome>.Success(new SignInOutcome { HomePath = homePath });
        }

        if (result.RequiresTwoFactor)
        {
            // The password was correct, but two-step sign-in is on (YY-7). Identity has stored a short-lived
            // "half signed in" cookie; the user is not signed in until the emailed code is checked.
            ApplicationUser? user = await _userManager.FindByEmailAsync(cleanEmail);
            if (user == null)
            {
                return ServiceResult<SignInOutcome>.Failure(InvalidLoginMessage);
            }

            await _accountSecurity.SendLoginCodeAsync(user);
            _logger.LogInformation("Sign-in code emailed to user {UserId}.", user.Id);
            return ServiceResult<SignInOutcome>.Success(new SignInOutcome { RequiresCode = true });
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Locked-out sign-in attempt for {Email}.", cleanEmail);
            return ServiceResult<SignInOutcome>.Failure(LockedOutMessage());
        }

        if (result.IsNotAllowed)
        {
            // Identity refuses deactivated accounts ...
            ServiceResult<SignInOutcome>? suspended = await CheckSuspendedAsync(cleanEmail, password);
            if (suspended != null)
            {
                return suspended;
            }
        }

        // Wrong password, unknown email, or an invited user who has not set a password yet:
        // all get the same message.
        return ServiceResult<SignInOutcome>.Failure(InvalidLoginMessage);
    }

    public async Task<ServiceResult<string>> SignInWithCodeAsync(string code, bool rememberMe)
    {
        ApplicationUser? user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            // The "half signed in" cookie has expired (about 5 minutes) or was never there.
            return ServiceResult<string>.Failure("Your sign-in has expired. Please sign in again.");
        }

        // rememberClient: false - never skip the code on this browser next time; every sign-in needs one.
        // A wrong code counts towards the same lockout as a wrong password.
        SignInResult result = await _signInManager.TwoFactorSignInAsync(
            TokenOptions.DefaultEmailProvider, code.Trim(), rememberMe, rememberClient: false);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {UserId} signed in with an email code.", user.Id);
            return ServiceResult<string>.Success(await GetHomePathAsync(user));
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {UserId} locked out after wrong sign-in codes.", user.Id);
            return ServiceResult<string>.Failure(LockedOutMessage());
        }

        return ServiceResult<string>.Failure(AccountSecurityService.InvalidCodeMessage);
    }

    public async Task<bool> HasPendingCodeSignInAsync()
    {
        ApplicationUser? user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        return user != null;
    }

    public async Task ResendLoginCodeAsync()
    {
        ApplicationUser? user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user != null)
        {
            await _accountSecurity.SendLoginCodeAsync(user);
        }
    }

    public async Task SignOutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    
    //For a suspended account: returns the suspended message only if the password is correct.
    //A wrong password still counts towards the lockout, so this cannot be used to guess passwords.
    //Returns null when the generic message should be shown.
    private async Task<ServiceResult<SignInOutcome>?> CheckSuspendedAsync(string email, string password)
    {
        ApplicationUser? user = await _userManager.FindByEmailAsync(email);
        if (user == null || user.IsActive)
        {
            return null;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return ServiceResult<SignInOutcome>.Failure(LockedOutMessage());
        }

        bool passwordIsCorrect = await _userManager.CheckPasswordAsync(user, password);
        if (!passwordIsCorrect)
        {
            await _userManager.AccessFailedAsync(user);
            return null;
        }

        _logger.LogWarning("Suspended account {UserId} tried to sign in.", user.Id);
        return ServiceResult<SignInOutcome>.Failure(SuspendedMessage);
    }

    private string LockedOutMessage()
    {
        int minutes = (int)Math.Ceiling(_identityOptions.Lockout.DefaultLockoutTimeSpan.TotalMinutes);
        return $"This account is temporarily locked after too many failed attempts. Try again in {minutes} minutes.";
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