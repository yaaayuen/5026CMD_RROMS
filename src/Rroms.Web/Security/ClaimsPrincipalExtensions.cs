using System.Globalization;
using System.Security.Claims;

namespace Rroms.Web.Security;

/// <summary>
/// Helpers for reading the signed-in user from the authentication cookie (YY-3).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    //Returns the Identity user ID of the signed-in user
    public static string GetUserId(this ClaimsPrincipal user)
    {
        //Identity stores the user's ID in the NameIdentifier claim when it creates the cookies
        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("GetUserId was called for a user who is not signed in.");
        }

        return userId;
    }
}