using System.Net;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-2 acceptance: login works for each role, fails safely, and resists common attacks.
/// </summary>
public class LoginTests : IClassFixture<TestWebApplicationFactory>
{
    private const string GoodPassword = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public LoginTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    //Gives each test its own account so tests never interfere with each other
    private static string UniqueEmail()
    {
        return $"user-{Guid.NewGuid():N}@threegrills.test";
    }

    [Theory]
    [InlineData(RoleNames.Staff, "/Staff")]
    [InlineData(RoleNames.Customer, "/Customer")]
    public async Task ValidLogin_RedirectsToRoleHome(string roleName, string expectedPath)
    {
        string email = UniqueEmail();
        await _factory.CreateUserAsync(email, GoodPassword, roleName);
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await FormHelper.LoginAsync(client, email, GoodPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedPath, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_EvenForTheCorrectPassword()
    {
        string email = UniqueEmail();
        await _factory.CreateUserAsync(email, GoodPassword, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await FormHelper.LoginAsync(client, email, "Wrong!Password1");
        }

        HttpResponseMessage response = await FormHelper.LoginAsync(client, email, GoodPassword);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporarily locked", html);
    }

    [Fact]
    public async Task ExternalReturnUrl_IsIgnored_ToPreventOpenRedirect()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await FormHelper.LoginAsync(
            client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword,
            returnUrl: "https://evil.example/steal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostWithoutAntiforgeryToken_IsRejected()
    {
        HttpClient client = _factory.CreateSecureClient();
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = TestWebApplicationFactory.AdminEmail,
            ["Input.Password"] = TestWebApplicationFactory.AdminPassword
        };

        //Posting directly, without first loading the form, means there is no token (a CSRF attempt).
        HttpResponseMessage response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}