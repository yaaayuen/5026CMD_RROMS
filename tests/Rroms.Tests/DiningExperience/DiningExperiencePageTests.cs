using System.Net;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
namespace Rroms.Tests.DiningExperience;

/// <summary>Checks the real HTTP pipeline, including role policies and antiforgery.</summary>
public class DiningExperiencePageTests
{
    [Theory]
    [InlineData("/Staff/Orders")]
    [InlineData("/Customer/Tracking")]
    [InlineData("/Customer/Feedback")]
    [InlineData("/Admin/Announcements")]
    [InlineData("/Admin/Feedback")]
    public async Task AnonymousCannotOpenProtectedPages(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task CustomerPagesRenderAndStaffPagesAreDenied()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.CreateUserAsync("alex-customer@test.local", "Test!Customer2026", RoleNames.Customer);
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, "alex-customer@test.local", "Test!Customer2026");
        foreach (string page in new[] { "/Customer/Tracking", "/Customer/Tracking?handler=Status", "/Customer/Feedback", "/Announcements" })
        { Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode); }
        var denied = await client.GetAsync("/Staff/Orders");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("AccessDenied", denied.Headers.Location!.ToString());
    }
    [Fact]
    public async Task AdminPagesRenderAndKitchenPostRequiresToken()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        foreach (string page in new[] { "/Admin/Announcements", "/Admin/Feedback", "/Staff/Orders", "/Staff/Orders?handler=Queue" })
        { Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode); }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Staff/Orders", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }
    [Fact]
    public async Task LocalChatWorksWithoutKeyAndNeedsAntiforgery()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        var fields = new Dictionary<string,string> { ["Question"] = "Show the menu" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Chat", new FormUrlEncodedContent(fields))).StatusCode);
        var response = await FormHelper.PostFormAsync(client, "/Chat", "/Chat", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/Menu", json);
        Assert.Contains("Local", json);
    }
}
