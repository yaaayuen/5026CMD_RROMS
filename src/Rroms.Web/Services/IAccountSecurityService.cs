using Rroms.Web.Common;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// One-time 6-digit email codes for setting a password and for two-step sign-in. Owner: YY (YY-7).
/// /// Nobody else ever chooses or sees a user's password: users set it themselves after proving
/// they own the email address. Codes are emailed only; they are never stored in the inbox or database.
/// </summary>

public interface IAccountSecurityService
{
    /// <summary>
    /// Emails a code for choosing a new password, if the address belongs to an active account.
    /// Used by "Forgot your password?" and by newly invited staff.
    /// Returns nothing on purpose: the page shows the same message either way (no user enumeration)
    /// </summary>
    Task RequestPasswordCodeAsync(string email);

    //Sets a new password if the emailed code is correct and unexpired. Changing the password retires the code
    Task<ServiceResult> ResetPasswordWithCodeAsync(string email, string code, string newPassword);

    //Emails the second-step code during sign-in (users with two-step sign-in turned on)
    Task SendLoginCodeAsync(ApplicationUser user);

    //True if the user has two-step sign-in turned on
    Task<bool> IsTwoStepEnabledAsync(string userId);

    //Emails a code the user must enter to turn two-step sign-in on
    Task<ServiceResult> SendTwoStepSetupCodeAsync(string userId);

    //Turns two-step sign-in on after checking the code (this also confirms the email address)
    Task<ServiceResult> EnableTwoStepAsync(string userId, string code);

    //Turns two-step sign-in off. Requires the current password
    Task<ServiceResult> DisableTwoStepAsync(string userId, string currentPassword);

    //True if two-step sign-in is compulsory for this user (staff and administrators, YY-9)
    Task<bool> IsTwoStepRequiredAsync(string userId);

    //Turns two-step sign-in on for a staff member or administrator who does not have it yet (YY-9)
    Task TurnOnRequiredTwoStepAsync(ApplicationUser user);

}
