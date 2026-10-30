using System.Net;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Services;

namespace Rroms.Tests.Admin;

/// <summary>
/// YY-10 acceptance: administrators can add dietary tags, and customers see them on their profile.
/// </summary>
public class DietaryTagAdminTests : IClassFixture<TestWebApplicationFactory>
{
    private const string GoodPassword = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public DietaryTagAdminTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    //Signs in as the seeded test administrator
    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        return client;
    }

    //Submits the "Add a tag" form
    private static Task<HttpResponseMessage> AddTagAsync(HttpClient client, string name)
    {
        return FormHelper.PostFormAsync(client, "/Admin/DietaryTags", "/Admin/DietaryTags",
            new Dictionary<string, string> { ["NewName"] = name });
    }

    [Fact]
    public async Task AdminAddsTag_AndCustomerCanChooseItOnProfile()
    {
        // A unique name, so the test can run many times against the same database.
        string tagName = ("Nuts " + Guid.NewGuid().ToString("N")).Substring(0, 12);
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage added = await AddTagAsync(admin, tagName);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        string email = $"tags-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, GoodPassword, RoleNames.Customer);
        HttpClient customer = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, email, GoodPassword);

        string profile = await customer.GetStringAsync("/Account/Profile");
        Assert.Contains(tagName, profile);
    }

    [Fact]
    public async Task AdminAddsTag_FromTheMenuItemsPage()
    {
        string tagName = ("Sesame " + Guid.NewGuid().ToString("N")).Substring(0, 12);
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage added = await FormHelper.PostFormAsync(admin, "/Admin/Menu", "/Admin/Menu?handler=AddTag",
            new Dictionary<string, string> { ["newTagName"] = tagName });

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains(tagName, await admin.GetStringAsync("/Admin/Menu"));
    }

    [Fact]
    public async Task DuplicateName_InAnyCase_IsRejected()
    {
        HttpClient admin = await AdminClientAsync();

        // "Dairy" is one of the three seeded tags.
        HttpResponseMessage response = await AddTagAsync(admin, "dAiRy");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(DietaryTagService.DuplicateNameMessage, html);
    }
}
