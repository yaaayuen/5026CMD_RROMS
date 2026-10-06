using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Rroms.Web.Models;

namespace Rroms.Web.Security;

/// <summary>
/// Identity's sign-in manager, extended so that deactivated accounts cannot sign in.
/// </summary>

public class ApplicationSignInManager : SignInManager<ApplicationUser>
{
    //Passes every dependency to the base class. 
    public ApplicationSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    //Allows sign-in only for active accounts, then applies identity's normal checks
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            Logger.LogWarning("User {Email} is deactivated and cannot sign in.", user.Id);
            return false;
        }

        return await base.CanSignInAsync(user);
    }
}