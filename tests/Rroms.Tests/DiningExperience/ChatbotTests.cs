using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rroms.Tests.History;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Local answers must never call NVIDIA; provider failure must have a usable fallback.</summary>
public class ChatbotTests
{
    [Theory]
    [InlineData("hello", null)]
    [InlineData("Show menu", "/Menu")]
    [InlineData("book a table", "/Customer/Reservations")]
    [InlineData("my order status", "/Customer/Tracking")]
    [InlineData("leave feedback", "/Customer/Feedback")]
    [InlineData("opening hours", "/Visit")]
    [InlineData("contact phone", "/Visit")]
    [InlineData("promotions", "/Announcements")]
    public async Task CommonQuestionsStayLocal(string question, string? expectedPage)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var handler = new Provider();
        var service = Service(scope.ServiceProvider, handler);
        var reply = await service.AskAsync(question, true);
        Assert.Equal("Local", reply.Source);
        Assert.Equal(expectedPage, reply.PageUrl);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task MenuUsesCurrentPromotionalPrice()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MenuItems.Add(new MenuItem { Name = "Chicken Chop", MenuCategoryId = 1, Price = 25,
            PromoPrice = 20, PromoStartUtc = DateTime.UtcNow.AddHours(-1), PromoEndUtc = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var handler = new Provider();
        var reply = await Service(scope.ServiceProvider, handler).AskAsync("How much is Chicken Chop?", true);
        Assert.Contains("20.00", reply.Answer);
        Assert.Equal("/Menu", reply.PageUrl);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task AvailabilityReadsTheBookingServiceWithoutCallingAi()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var date = Rroms.Web.Common.MalaysiaTime.FromUtc(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");
        var handler = new Provider();
        var reply = await Service(scope.ServiceProvider, handler).AskAsync($"table {date} 19:00 for 2", true);
        Assert.Contains("suitable tables", reply.Answer);
        Assert.Equal("/Customer/Reservations", reply.PageUrl);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("how do I reserve a table", "press Book a table")]
    [InlineData("how to cancel my booking", "Cancel booking")]
    [InlineData("how do I change my booking", "confirmed straight away")]
    [InlineData("how do I leave feedback", "Submit feedback")]
    public async Task HowToQuestionsExplainTheSteps(string question, string expectedStep)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync(question, true);
        Assert.Contains(reply.Steps, step => step.Contains(expectedStep));
    }
    [Fact]
    public async Task BookingStepsMatchAutoConfirmation()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync("how do I reserve a table", true);
        Assert.Contains(reply.Steps, step => step.Contains("confirmed straight away"));
        Assert.DoesNotContain(reply.Steps, step => step.Contains("Pending") || step.Contains("confirm again"));
    }
    [Fact]
    public async Task GuestStillSeesBookingStepsAfterSigningIn()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync("how do I book a table", false);
        Assert.Equal("/Account/Login", reply.PageUrl);
        Assert.StartsWith("Sign in", reply.Steps[0]);
        Assert.True(reply.Steps.Count > 1);
    }
    [Fact]
    public async Task GuestIsSentToLoginForPersonalRecords()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync("my order", false);
        Assert.Equal("/Account/Login", reply.PageUrl);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("error")]
    [InlineData("malformed")]
    [InlineData("timeout")]
    public async Task ProviderProblemsReturnLocalFallback(string mode)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var handler = new Provider { Mode = mode };
        var reply = await Service(scope.ServiceProvider, handler, mode == "missing" ? "" : "test-placeholder").AskAsync("Explain the difference between grilling and roasting", true);
        Assert.Equal("Local", reply.Source);
        Assert.Null(reply.PageUrl);
        Assert.Equal(mode == "missing" ? 0 : 1, handler.Calls);
    }
    [Fact]
    public async Task AiAnswerCannotSupplyNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var handler = new Provider();
        var reply = await Service(scope.ServiceProvider, handler).AskAsync("Explain grilling", true);
        Assert.Equal("AI", reply.Source);
        Assert.Null(reply.PageUrl);
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task AiUsesAModelNvidiaStillServesByDefault()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var handler = new Provider();
        await Service(scope.ServiceProvider, handler).AskAsync("Explain grilling", true);
        Assert.Equal("meta/llama-3.2-11b-vision-instruct", handler.Model);
        Assert.Equal(12, new ChatbotOptions().TimeoutSeconds);
    }
    [Fact]
    public async Task WhereIsMyOrderReadsOnlyTheCustomersOwnUnfinishedOrders()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationUser alice = await OrderTestData.AddCustomerAsync(db);
        ApplicationUser bob = await OrderTestData.AddCustomerAsync(db);
        Reservation seated = await HistoryTestData.AddReservationAsync(db, alice.Id, DateTime.UtcNow, ReservationStatus.Seated);
        Reservation later = await HistoryTestData.AddReservationAsync(db, alice.Id, DateTime.UtcNow.AddDays(2), ReservationStatus.Confirmed);
        Reservation other = await HistoryTestData.AddReservationAsync(db, bob.Id, DateTime.UtcNow, ReservationStatus.Seated);
        Order cooking = AddOrder(db, seated, OrderStatus.Preparing);
        Order saved = AddOrder(db, later, OrderStatus.Placed);
        Order done = AddOrder(db, seated, OrderStatus.Completed);
        Order bobs = AddOrder(db, other, OrderStatus.ReadyToServe);
        await db.SaveChangesAsync();
        var handler = new Provider();
        var reply = await Service(scope.ServiceProvider, handler).AskAsync("Where is my order?", true, default, alice.Id);
        Assert.Contains($"Order #{cooking.Id}: Preparing", reply.Answer);
        Assert.Contains($"Order #{saved.Id}: Placed (saved; the kitchen receives it after you are seated)", reply.Answer);
        Assert.DoesNotContain($"#{done.Id}:", reply.Answer);
        Assert.DoesNotContain($"#{bobs.Id}:", reply.Answer);
        Assert.Equal("/Customer/Tracking", reply.PageUrl);
        Assert.Equal(0, handler.Calls);
        var bobReply = await Service(scope.ServiceProvider, handler).AskAsync("is my food ready", true, default, bob.Id);
        Assert.Contains($"Order #{bobs.Id}: Ready to serve", bobReply.Answer);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task OrderQuestionsWithoutActiveOrdersOrAsHowToStayHelpful()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationUser alice = await OrderTestData.AddCustomerAsync(db);
        var service = Service(scope.ServiceProvider, new Provider());
        Assert.Contains("no orders in progress", (await service.AskAsync("my order status", true, default, alice.Id)).Answer);
        var howTo = await service.AskAsync("how do I order food", true, default, alice.Id);
        Assert.Contains(howTo.Steps, step => step.Contains("Place order"));
    }
    [Fact]
    public async Task OfferQuestionsListOnlyRunningPromotions()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = Service(scope.ServiceProvider, new Provider());
        Assert.Contains("no offers running", (await service.AskAsync("any promotions?", true)).Answer);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        DateTime now = DateTime.UtcNow;
        db.Announcements.AddRange(
            new Announcement { Title = "Chat lunch deal", Message = "m", IsPromotion = true, IsPublished = true, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(3), UpdatedAtUtc = now },
            new Announcement { Title = "Chat holiday hours", Message = "m", IsPublished = true, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(3), UpdatedAtUtc = now },
            new Announcement { Title = "Chat old deal", Message = "m", IsPromotion = true, IsPublished = true, StartUtc = now.AddDays(-9), EndUtc = now.AddDays(-2), UpdatedAtUtc = now },
            new Announcement { Title = "Chat draft deal", Message = "m", IsPromotion = true, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(3), UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        var reply = await service.AskAsync("Any deals this week?", true);
        Assert.Contains("Chat lunch deal (until ", reply.Answer);
        Assert.DoesNotContain("Chat holiday hours", reply.Answer);
        Assert.DoesNotContain("Chat old deal", reply.Answer);
        Assert.DoesNotContain("Chat draft deal", reply.Answer);
        Assert.Equal("/Announcements", reply.PageUrl);
    }

    [Fact]
    public async Task OfferQuestionsAlsoNameRunningMenuDiscounts()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MenuDiscounts.AddRange(
            new MenuDiscount { Name = "Chat lunch", Percent = 15m, Scope = DiscountScope.WholeMenu, IsActive = true },
            new MenuDiscount { Name = "Chat paused", Percent = 30m, Scope = DiscountScope.WholeMenu, IsActive = false });
        await db.SaveChangesAsync();
        var service = Service(scope.ServiceProvider, new Provider());
        var onlyDiscount = await service.AskAsync("any discounts?", true);
        Assert.Contains("Menu discounts now: Chat lunch: 15% off the whole menu", onlyDiscount.Answer);
        Assert.DoesNotContain("Chat paused", onlyDiscount.Answer);
        Assert.DoesNotContain("no offers", onlyDiscount.Answer);
        Assert.Equal("/Menu", onlyDiscount.PageUrl);
        DateTime now = DateTime.UtcNow;
        db.Announcements.Add(new Announcement { Title = "Chat birthday treat", Message = "m", IsPromotion = true, IsPublished = true,
            StartUtc = now.AddDays(-1), EndUtc = now.AddDays(3), UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        var both = await service.AskAsync("any deals?", true);
        Assert.Contains("Chat lunch: 15% off", both.Answer);
        Assert.Contains("Current offers: Chat birthday treat (until ", both.Answer);
        Assert.Equal("/Announcements", both.PageUrl);
    }

    /// <summary>Questions whose words could fit more than one topic, and the page each must reach without the AI.</summary>
    [Theory]
    [InlineData("Do you offer vegetarian food?", "/Menu")]
    [InlineData("Show me my bookings", "/Customer/Reservations")]
    [InlineData("I want to read the reviews", "/Customer/Feedback")]
    [InlineData("What are the prices?", "/Menu")]
    [InlineData("Where are you located?", "/Visit")]
    [InlineData("What time do you close on Sunday?", "/Visit")]
    [InlineData("Is my food ready?", "/Customer/Tracking")]
    [InlineData("Can I open my profile?", "/Account/Profile")]
    [InlineData("How do I cancel my reservations?", "/Customer/Reservations")]
    [InlineData("I have a nut allergy, which dish is safe?", "/Menu")]
    [InlineData("Any discounts today?", "/Announcements")]
    [InlineData("Thanks!", null)]
    [InlineData("Can I pay by card?", null)]
    public async Task AmbiguousQuestionsReachTheRightTopicLocally(string question, string? expectedPage)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var handler = new Provider();
        var reply = await Service(scope.ServiceProvider, handler).AskAsync(question, true);
        Assert.Equal("Local", reply.Source);
        Assert.Equal(expectedPage, reply.PageUrl);
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData("Do you have vegetables?")]
    [InlineData("Is the dessert comfortable to share?")]
    public async Task WordsInsideLongerWordsDoNotTriggerATopic(string question)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync(question, true);
        Assert.NotEqual("/Customer/Reservations", reply.PageUrl);
    }
    private static Order AddOrder(ApplicationDbContext db, Reservation visit, OrderStatus status)
    {
        var order = new Order { ReservationId = visit.Id, CustomerId = visit.CustomerId, Status = status, TotalAmount = 20m,
            PlacedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.Orders.Add(order);
        return order;
    }
    private static ChatbotService Service(IServiceProvider services, Provider handler, string key = "test-placeholder")
    {
        return new ChatbotService(services.GetRequiredService<IMenuService>(), services.GetRequiredService<IRestaurantService>(), services.GetRequiredService<IReservationService>(),
            services.GetRequiredService<IAnnouncementService>(), services.GetRequiredService<IOrderProcessingService>(),
            services.GetRequiredService<IDiscountService>(), new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new ChatbotOptions { ApiKey = key }), NullLogger<ChatbotService>.Instance);
    }
    private class Provider : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Mode { get; set; } = "ok";
        public string? Model { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https://integrate.api.nvidia.com/v1/chat/completions", request.RequestUri!.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Model = body.RootElement.GetProperty("model").GetString();
            if (Mode == "timeout") { throw new TaskCanceledException(); }
            return new HttpResponseMessage(Mode == "error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(Mode == "malformed" ? "{}" : "{\"choices\":[{\"message\":{\"content\":\"Grilling uses direct heat.\"}}]}")
            };
        }
    }
}
