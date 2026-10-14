using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Registration, sign-in and sign-out. Owner: YY (Sprint 1, YY-2).
/// Pages call this interface instead of using Identity's UserManager and SignInManager directly,
/// so the security rules live in one tested class.
/// </summary>

public interface IAccountService
{
    //Creates a new customer account, adds it to the Customer role and signs the user in.
    Task<ServiceResult<ApplicationUser>> RegisterCustomerAsync(string fullName, string email, string? phoneNumber, string password);

    //Checks the email and password, if valid, issues the authentication cookies
    Task<ServiceResult<SignInOutcome>> SignInAsync(string email, string password, bool rememberMe);

    //Second step of sign-in: checks the emailed code and issues the authentication cookie (YY-7)
    Task<ServiceResult<string>> SignInWithCodeAsync(string code, bool rememberMe);

    //True if a correct password was entered and the browser is waiting for the code (YY-7)
    Task<bool> HasPendingCodeSignInAsync();

    //Emails a fresh sign-in code to the user waiting at the code page (YY-7)
    Task ResendLoginCodeAsync();

    //Removes the authentication cookie
    Task SignOutAsync();
}
