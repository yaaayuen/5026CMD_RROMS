using System.Net;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;

namespace Rroms.Tests.Security;

/// <summary>
/// YY-3 acceptance: the role x area access matrix. Rubric items 1 and 8.
/// </summary>

public class AccessControlTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public AccessControlTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    //Creates a user with the role (if any) and returns a client signed in as them
    private async Task<HttpClient> ClientForRoleAsync(string? roleName)
    {
        HttpClient client = _factory.CreateSecureClient();
        if (roleName == null)
        {
            return client;
        }

        if (roleName == RoleNames.Administrator)
        {
            await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
            return client;
        }

        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, roleName);
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Staff")]
    [InlineData("/Customer")]
    [InlineData("/Account/Profile")]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = await ClientForRoleAsync(null);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData(RoleNames.Customer, "/Admin")]
    [InlineData(RoleNames.Customer, "/Staff")]
    [InlineData(RoleNames.Staff, "/Admin")]
    [InlineData(RoleNames.Staff, "/Customer")]
    [InlineData(RoleNames.Administrator, "/Customer")]
    public async Task OtherRole_IsDenied(string roleName, string url)
    {
        HttpClient client = await ClientForRoleAsync(roleName);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }
}