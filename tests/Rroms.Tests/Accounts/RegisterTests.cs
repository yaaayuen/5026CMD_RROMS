using System.Net;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Models;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-2 acceptance: the server enforces the password policy when a customer registers.
/// </summary>
public class RegisterTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RegisterTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail()
    {
        return $"new-{Guid.NewGuid():N}@threegrills.test";
    }

    private static Dictionary<string, string> RegistrationFields(string email, string password, string confirmPassword)
    {
        return new Dictionary<string, string>
        {
            ["Input.FullName"] = "Aisha Rahman",
            ["Input.Email"] = email,
            ["Input.PhoneNumber"] = "012-345 6789",
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = confirmPassword
        };
    }

    private Task<HttpResponseMessage> RegisterAsync(HttpClient client, Dictionary<string, string> fields)
    {
        return FormHelper.PostFormAsync(client, "/Account/Register", "/Account/Register", fields);
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    [Fact]
    public async Task WeakPassword_IsRejected_AndNoAccountIsCreated()
    {
        string email = UniqueEmail();
        HttpClient client = _factory.CreateSecureClient();

        //Long enough, but no upper case, digit or symbol: fails the Identity policy on the server.
        HttpResponseMessage response = await RegisterAsync(client, RegistrationFields(email, "allletterslong", "allletterslong"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindUserAsync(email));
    }
}