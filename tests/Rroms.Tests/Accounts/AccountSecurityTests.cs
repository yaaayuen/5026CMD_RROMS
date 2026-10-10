using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;
using Rroms.Web.Services.Email;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-7 acceptance: passwords are set with an emailed 6-digit code, and two-step sign-in needs that code.
/// </summary>
public class AccountSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private const string OldPassword = "Grill!Test2026";
    private const string NewPassword = "Fresh!Grill2027";

    //The code sits alone inside an HTML element in the email
    private static readonly Regex CodePattern = new Regex(@">(\d{6})<");

    private readonly TestWebApplicationFactory _factory;

    public AccountSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail()
    {
        return $"security-{Guid.NewGuid():N}@threegrills.test";
    }

    //Reads the newest code emailed to an address, like the user opening their inbox
    private string LatestCodeFor(string email)
    {
        EmailMessage message = _factory.EmailQueue.Queued
            .Last(queued => queued.ToAddress == email && queued.Subject.Contains("verification code"));
        return CodePattern.Match(message.HtmlBody).Groups[1].Value;
    }

    private static Dictionary<string, string> ResetFields(string email, string code, string password)
    {
        return new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.NewPassword"] = password,
            ["Input.ConfirmPassword"] = password
        };
    }

    [Fact]
    public async Task ForgotPassword_SameResponseForUnknownEmail_AndOnlyRealAccountsGetACode()
    {
        string realEmail = UniqueEmail();
        string unknownEmail = UniqueEmail();
        await _factory.CreateUserAsync(realEmail, OldPassword, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage realResponse = await FormHelper.PostFormAsync(client, "/Account/ForgotPassword", "/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = realEmail });
        HttpResponseMessage unknownResponse = await FormHelper.PostFormAsync(client, "/Account/ForgotPassword", "/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = unknownEmail });

        // Same status and same next page, so an attacker cannot tell which email is registered.
        Assert.Equal(HttpStatusCode.Redirect, realResponse.StatusCode);
        Assert.Equal(realResponse.StatusCode, unknownResponse.StatusCode);
        Assert.StartsWith("/Account/ResetPassword", unknownResponse.Headers.Location?.OriginalString);

        Assert.Contains(_factory.EmailQueue.Queued, message => message.ToAddress == realEmail);
        Assert.DoesNotContain(_factory.EmailQueue.Queued, message => message.ToAddress == unknownEmail);
    }

    [Fact]
    public async Task ResetPassword_WithEmailedCode_ChangesPassword_AndTheCodeCannotBeReused()
    {
        string email = UniqueEmail();
        await _factory.CreateUserAsync(email, OldPassword, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();

        await FormHelper.PostFormAsync(client, "/Account/ForgotPassword", "/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = email });
        string code = LatestCodeFor(email);

        HttpResponseMessage reset = await FormHelper.PostFormAsync(client, "/Account/ResetPassword", "/Account/ResetPassword",
            ResetFields(email, code, NewPassword));
        Assert.Equal("/Account/Login", reset.Headers.Location?.OriginalString);

        // The new password works.
        HttpResponseMessage login = await FormHelper.LoginAsync(_factory.CreateSecureClient(), email, NewPassword);
        Assert.Equal("/Customer", login.Headers.Location?.OriginalString);

        // Replaying the same code fails: changing the password retired it.
        HttpResponseMessage replay = await FormHelper.PostFormAsync(client, "/Account/ResetPassword", "/Account/ResetPassword",
            ResetFields(email, code, "Another!Grill2028"));
        string html = await replay.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains(AccountSecurityService.InvalidCodeMessage, html);
    }

    [Fact]
    public async Task TwoStepUser_IsNotSignedIn_UntilTheEmailedCodeIsEntered()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, OldPassword, RoleNames.Customer);
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser stored = (await userManager.FindByIdAsync(user.Id))!;
            stored.EmailConfirmed = true;
            await userManager.UpdateAsync(stored);
            await userManager.SetTwoFactorEnabledAsync(stored, true);
        }

        HttpClient client = _factory.CreateSecureClient();

        // Correct password: sent to the code page, not to the dashboard.
        HttpResponseMessage login = await FormHelper.LoginAsync(client, email, OldPassword);
        Assert.StartsWith("/Account/LoginWithCode", login.Headers.Location?.OriginalString);

        // Still not signed in: the customer area sends the browser to the login page.
        HttpResponseMessage beforeCode = await client.GetAsync("/Customer");
        Assert.Contains("/Account/Login", beforeCode.Headers.Location?.OriginalString);

        HttpResponseMessage withCode = await FormHelper.PostFormAsync(client, "/Account/LoginWithCode", "/Account/LoginWithCode",
            new Dictionary<string, string> { ["Code"] = LatestCodeFor(email) });
        Assert.Equal("/Customer", withCode.Headers.Location?.OriginalString);
    }
}
