using Rroms.Web.Common;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// Registration, sign-in and sign-out. Owner: YY (Sprint 1, YY-2).
/// Pages call this interface instead of using Identity's UserManager and SignInManager directly,
/// so the security rules live in one tested class.
/// </summary>

public interface IAccountServices
{
    //Creates a new customer account, adds it to the Customer role and signs the user in.
    Task<ServiceResult<ApplicationUser>> RegisterCustomerAsync(string fullName, string email, string? phoneNumber, string password);

    //Checks the email and password, if valid, issues the authentication cookies
    Task<ServiceResult<string>> SignInAsync(string email, string password, bool rememberMe);

    //Removes the authentication cookie
    Task SignOutAsync();
}
