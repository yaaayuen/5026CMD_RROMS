using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using ImageMagick;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-1 acceptance for the admin menu pages: who can open them, and that the forms really save
/// (or refuse) through the running application. Owner: YJ.
/// </summary>
/// <remarks>
/// These are integration tests in the same style as WL's RestaurantPagesTests: the whole app starts in memory
/// (TestWebApplicationFactory), tests sign in through the real login form and send real HTTP requests, so
/// routing, authorization, antiforgery tokens, model binding and the services are exercised together.
/// All tests in this class share one database, so every dish gets a unique name.
/// </remarks>
public class MenuPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private const string ListUrl = "/Admin/Menu";
    private const string EditUrl = "/Admin/Menu/Edit";

    /// <summary>A dish Id that no test creates.</summary>
    private const int MissingDishId = 999999;

    /// <summary>Id of the seeded "Starters" category.</summary>
    private const int StartersId = 1;

    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    private const uint PhotoWidth = 100;
    private const uint PhotoHeight = 50;

    private const decimal NormalPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Theory]
    [InlineData(ListUrl)]
    [InlineData(EditUrl)]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Customers and staff are signed in but are not administrators, so they are refused.</summary>
    [Theory]
    [InlineData(RoleNames.Customer, ListUrl)]
    [InlineData(RoleNames.Customer, EditUrl)]
    [InlineData(RoleNames.Staff, ListUrl)]
    [InlineData(RoleNames.Staff, EditUrl)]
    public async Task NonAdministrator_IsDenied(string roleName, string url)
    {
        HttpClient client = await SignInAsNewUserAsync(roleName);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    /// <summary>The administrator can open both pages and sees their headings.</summary>
    [Theory]
    [InlineData(ListUrl, "All dishes")]
    [InlineData(EditUrl, "Dish details")]
    public async Task Administrator_CanOpenPage(string url, string expectedText)
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync(url);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html);
    }

    /// <summary>The "Menu items" tile on the admin dashboard links to the menu list.</summary>
    [Fact]
    public async Task AdminDashboard_LinksToTheMenuList()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync("/Admin");

        Assert.Contains($"href=\"{ListUrl}\"", html);
    }

    // ============================== Adding dishes ==============================//

    /// <summary>A valid dish is saved, the browser is redirected, and the dish shows in the list.</summary>
    [Fact]
    public async Task Add_ValidDish_AppearsInTheList()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        Dictionary<string, string> fields = ValidDishFields(name);

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        string listHtml = await client.GetStringAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(name, listHtml);
    }

    /// <summary>A price of zero is refused and the service's message is shown on the form.</summary>
    [Fact]
    public async Task Add_PriceZero_ShowsServiceError()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        Dictionary<string, string> fields = ValidDishFields(NewDishName());
        fields["Input.Price"] = "0";

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MenuService.PriceMessage, html);
    }

    /// <summary>The dish form no longer has an offer section: discounts are set on the Promotions page.</summary>
    [Fact]
    public async Task EditPage_HasNoOfferFields()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync(EditUrl);

        Assert.DoesNotContain("Offer price", html);
        Assert.DoesNotContain("Offer starts", html);
        Assert.DoesNotContain("PromoStartLocal", html);
    }

    /// <summary>An offer sent by hand (not through the form) is ignored: the dish is saved without one.</summary>
    [Fact]
    public async Task Add_OfferSentByHand_IsIgnored()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        Dictionary<string, string> fields = ValidDishFields(name);
        fields["Input.PromoPrice"] = "15.00";
        fields["Input.PromoStartUtc"] = DateTime.UtcNow.AddDays(1).ToString("o", CultureInfo.InvariantCulture);
        fields["Input.PromoEndUtc"] = DateTime.UtcNow.AddDays(8).ToString("o", CultureInfo.InvariantCulture);

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(saved);
        Assert.Null(saved.PromoPrice);
        Assert.Null(saved.PromoStartUtc);
        Assert.Null(saved.PromoEndUtc);
    }

    /// <summary>The same dish name in another category is refused, because names are unique on the whole menu.</summary>
    [Fact]
    public async Task Add_SameNameAsADishInAnotherCategory_ShowsServiceError()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        Dictionary<string, string> first = ValidDishFields(name);
        first["Input.MenuCategoryId"] = GrillsId.ToString();
        await FormHelper.PostFormAsync(client, EditUrl, EditUrl, first);

        Dictionary<string, string> second = ValidDishFields(name);
        second["Input.MenuCategoryId"] = StartersId.ToString();

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, second);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MenuService.DuplicateNameMessage, html);
    }

    // ============================== Photos ==============================//

    /// <summary>A real image is saved on disk, linked to the dish, and removed again when the dish is deleted.</summary>
    [Fact]
    public async Task Add_WithPhoto_SavesTheFileAndDeleteRemovesIt()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        byte[] png = CreatePngBytes();

        HttpResponseMessage response = await PostFormWithFileAsync(
            client, EditUrl, EditUrl, ValidDishFields(name), "Photo", "burger.png", png, "image/png");
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(saved.ImageFileName);
        string photoPath = GetPhotoPath(saved.ImageFileName);
        Assert.True(File.Exists(photoPath));

        // Cleanup that doubles as a check: deleting the dish must also delete its photo file.
        await DeleteDishAsync(saved.Id);
        Assert.False(File.Exists(photoPath));
    }

    /// <summary>
    /// A text file renamed to .jpg is refused, but the dish itself is kept: the administrator is sent to the
    /// edit page and told what went wrong with the photo.
    /// </summary>
    [Fact]
    public async Task Add_TextFileRenamedToJpg_SavesTheDishButWarnsAboutThePhoto()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        byte[] notAnImage = Encoding.UTF8.GetBytes("This is just text, not a picture.");

        HttpResponseMessage response = await PostFormWithFileAsync(
            client, EditUrl, EditUrl, ValidDishFields(name), "Photo", "fake.jpg", notAnImage, "image/jpeg");
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"{EditUrl}/", response.Headers.Location?.ToString());
        Assert.NotNull(saved);
        Assert.Null(saved.ImageFileName);

        string editHtml = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains(MenuImageStorage.NotAnImageMessage, editHtml);
    }

    // ============================== Editing, filtering, availability and delete ==============================//

    /// <summary>Opening the edit page of a dish that does not exist returns 404 Not Found.</summary>
    [Fact]
    public async Task Edit_MissingDish_ReturnsNotFound()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync($"{EditUrl}/{MissingDishId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The edit page of an existing dish shows that dish's details.</summary>
    [Fact]
    public async Task Edit_ExistingDish_ShowsItsName()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        int id = await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{EditUrl}/{id}");

        Assert.Contains(name, html);
    }

    /// <summary>The category filter shows only dishes of the chosen category.</summary>
    [Fact]
    public async Task List_FilteredByCategory_ShowsOnlyThatCategory()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string starterName = NewDishName();
        string grillName = NewDishName();
        await CreateDishAsync(starterName, StartersId);
        await CreateDishAsync(grillName, GrillsId);

        string html = await client.GetStringAsync($"{ListUrl}?categoryId={GrillsId}");

        Assert.Contains(grillName, html);
        Assert.DoesNotContain(starterName, html);
    }

    /// <summary>The "Mark unavailable" button turns ordering off for the dish.</summary>
    [Fact]
    public async Task Toggle_MarkUnavailable_TurnsOrderingOff()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        int id = await CreateDishAsync(name);
        var fields = new Dictionary<string, string>
        {
            ["makeAvailable"] = "false"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, ListUrl, $"{ListUrl}?handler=Toggle&id={id}", fields);
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(saved);
        Assert.False(saved.IsAvailable);
    }

    /// <summary>The Delete button removes the dish.</summary>
    [Fact]
    public async Task Delete_RemovesTheDish()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewDishName();
        int id = await CreateDishAsync(name);
        var fields = new Dictionary<string, string>();

        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, ListUrl, $"{ListUrl}?handler=Delete&id={id}", fields);
        MenuItem? gone = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(gone);
    }

    // ============================== Helpers ==============================//

    /// <summary>A name no other test uses, so tests that share the database never clash.</summary>
    private static string NewDishName()
    {
        return "Test dish " + Guid.NewGuid().ToString("N");
    }

    /// <summary>The form fields for a valid dish, in the names the page expects.</summary>
    /// <param name="name">The dish name.</param>
    private static Dictionary<string, string> ValidDishFields(string name)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Description"] = "Made for a test",
            ["Input.MenuCategoryId"] = GrillsId.ToString(),
            ["Input.Price"] = "20.00",
            ["Input.IsAvailable"] = "true"
        };

        return fields;
    }

    /// <summary>Builds the bytes of a small real PNG image.</summary>
    private static byte[] CreatePngBytes()
    {
        using MagickImage image = new MagickImage(MagickColors.White, PhotoWidth, PhotoHeight);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>
    /// Submits a form that includes a file, the way a browser does (multipart). The antiforgery token is read
    /// from the form page first, exactly like FormHelper does for plain forms.
    /// </summary>
    private static async Task<HttpResponseMessage> PostFormWithFileAsync(
        HttpClient client,
        string formPageUrl,
        string postUrl,
        IDictionary<string, string> fields,
        string fileFieldName,
        string fileName,
        byte[] fileBytes,
        string contentType)
    {
        string token = await FormHelper.GetAntiforgeryTokenAsync(client, formPageUrl);

        using MultipartFormDataContent content = new MultipartFormDataContent();

        foreach (KeyValuePair<string, string> field in fields)
        {
            content.Add(new StringContent(field.Value), field.Key);
        }

        content.Add(new StringContent(token), FormHelper.TokenFieldName);

        ByteArrayContent file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, fileFieldName, fileName);

        return await client.PostAsync(postUrl, content);
    }

    /// <summary>Returns a client signed in as the administrator the test seeder creates.</summary>
    private async Task<HttpClient> SignInAsAdministratorAsync()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        return client;
    }

    /// <summary>Creates a new user with the given role and returns a client signed in as them.</summary>
    /// <param name="roleName">For example <see cref="RoleNames.Customer"/>.</param>
    private async Task<HttpClient> SignInAsNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, roleName);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }

    /// <summary>Creates a dish straight through the service and returns its Id.</summary>
    private async Task<int> CreateDishAsync(string name, int categoryId = GrillsId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        var input = new MenuItemInput
        {
            Name = name,
            Description = "Made for a test",
            MenuCategoryId = categoryId,
            Price = NormalPrice,
            IsAvailable = true
        };

        ServiceResult<int> result = await service.CreateMenuItemAsync(input);
        Assert.True(result.Succeeded);

        return result.Value;
    }

    /// <summary>Deletes a dish through the service (which also deletes its photo file).</summary>
    private async Task DeleteDishAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        await service.DeleteMenuItemAsync(id);
    }

    /// <summary>Reads a dish back from the service by its unique name, or null when it does not exist.</summary>
    private async Task<MenuItem?> FindDishAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        IReadOnlyList<MenuItem> items = await service.GetMenuItemsAsync(null);

        return items.FirstOrDefault(item => item.Name == name);
    }

    /// <summary>Works out where the running test application stores a photo on disk.</summary>
    /// <param name="storedName">The photo file name saved on the dish.</param>
    private string GetPhotoPath(string storedName)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IWebHostEnvironment environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        MenuImageOptions options = scope.ServiceProvider.GetRequiredService<IOptions<MenuImageOptions>>().Value;

        return Path.Combine(environment.WebRootPath, options.RelativeFolder, storedName);
    }
}
