using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.History;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
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
    [Fact]
    public async Task OrdersAppearUnderTheirOwnVisitWithoutReservationNumbers()
    {
        await using var tracking = await TrackingAsync();
        Reservation upcoming = await tracking.VisitAsync(DateTime.UtcNow.AddDays(3), ReservationStatus.Confirmed);
        Reservation finished = await tracking.VisitAsync(DateTime.UtcNow.AddDays(-10), ReservationStatus.Completed);
        await tracking.OrderAsync(upcoming, OrderStatus.Placed, "Tracking Lamb Chop");
        await tracking.OrderAsync(finished, OrderStatus.Completed, "Tracking Mushroom Soup");
        string html = await tracking.StatusHtmlAsync();
        Assert.DoesNotContain("Reservation #", html);
        int upcomingAt = html.IndexOf($"/Customer/Reservations/Details/{upcoming.Id}\"");
        int pastAt = html.IndexOf("id=\"track-past\"");
        int finishedAt = html.IndexOf($"/Customer/Reservations/Details/{finished.Id}\"");
        int lamb = html.IndexOf("Tracking Lamb Chop");
        int soup = html.IndexOf("Tracking Mushroom Soup");
        Assert.True(upcomingAt < lamb && lamb < pastAt, "The upcoming order should sit under the upcoming visit.");
        Assert.True(finishedAt < soup, "The past order should sit under its past visit.");
    }
    [Fact]
    public async Task StepperMarksTheCurrentStatusWithAriaCurrent()
    {
        await using var tracking = await TrackingAsync();
        Reservation seated = await tracking.VisitAsync(DateTime.UtcNow, ReservationStatus.Seated);
        await tracking.OrderAsync(seated, OrderStatus.ReadyToServe, "Tracking Ribeye");
        string html = await tracking.StatusHtmlAsync();
        Assert.Single(Regex.Matches(html, "aria-current=\"step\""));
        Assert.Matches(new Regex("<li class=\"is-current\" aria-current=\"step\">\\s*<span[^>]*>3</span>\\s*Ready to serve"), html);
        Assert.Equal(2, Regex.Matches(html, "<li class=\"is-done\">").Count);
        Assert.DoesNotContain("ReadyToServe", html);
    }
    [Fact]
    public async Task CancelledOrderShowsABadgeInsteadOfTheStepper()
    {
        await using var tracking = await TrackingAsync();
        Reservation upcoming = await tracking.VisitAsync(DateTime.UtcNow.AddDays(2), ReservationStatus.Confirmed);
        await tracking.OrderAsync(upcoming, OrderStatus.Cancelled, "Tracking Pasta");
        string html = await tracking.StatusHtmlAsync();
        Assert.Contains("<span class=\"badge text-bg-danger\">Cancelled</span>", html);
        Assert.DoesNotContain("order-steps", html);
        Assert.Contains("Tracking Pasta", html);
    }

    /// <summary>A signed-in customer with their own visits and orders, for the Track my visit tests.</summary>
    private sealed class Tracking : IAsyncDisposable
    {
        public required TestWebApplicationFactory Factory { get; init; }
        public required HttpClient Client { get; init; }
        public required string CustomerId { get; init; }
        public async Task<Reservation> VisitAsync(DateTime startUtc, ReservationStatus status)
        {
            using IServiceScope scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await HistoryTestData.AddReservationAsync(context, CustomerId, startUtc, status);
        }
        public async Task OrderAsync(Reservation visit, OrderStatus status, string dishName)
        {
            using IServiceScope scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MenuItem dish = await OrderTestData.AddDishAsync(context, dishName);
            var order = new Order { ReservationId = visit.Id, CustomerId = CustomerId, Status = status, TotalAmount = 40m,
                PlacedAtUtc = visit.StartUtc, UpdatedAtUtc = visit.StartUtc };
            order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 2, UnitPrice = 20m });
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }
        public Task<string> StatusHtmlAsync() => Client.GetStringAsync("/Customer/Tracking?handler=Status");
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }
    private static async Task<Tracking> TrackingAsync()
    {
        var factory = new TestWebApplicationFactory();
        ApplicationUser customer = await factory.CreateUserAsync("alex-tracking@test.local", "Test!Customer2026", RoleNames.Customer);
        HttpClient client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, "alex-tracking@test.local", "Test!Customer2026");
        return new Tracking { Factory = factory, Client = client, CustomerId = customer.Id };
    }
}
