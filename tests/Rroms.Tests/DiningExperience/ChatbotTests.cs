using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rroms.Tests.Infrastructure;
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
    [InlineData("opening hours", null)]
    [InlineData("contact phone", null)]
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
    [InlineData("how do I leave feedback", "Submit feedback")]
    public async Task HowToQuestionsExplainTheSteps(string question, string expectedStep)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider, new Provider()).AskAsync(question, true);
        Assert.Contains(reply.Steps, step => step.Contains(expectedStep));
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
    private static ChatbotService Service(IServiceProvider services, Provider handler, string key = "test-placeholder")
    {
        return new ChatbotService(services.GetRequiredService<IMenuService>(), services.GetRequiredService<IRestaurantService>(), services.GetRequiredService<IReservationService>(),
            new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new ChatbotOptions { ApiKey = key }), NullLogger<ChatbotService>.Instance);
    }
    private class Provider : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Mode { get; set; } = "ok";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https://integrate.api.nvidia.com/v1/chat/completions", request.RequestUri!.ToString());
            if (Mode == "timeout") { throw new TaskCanceledException(); }
            return Task.FromResult(new HttpResponseMessage(Mode == "error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(Mode == "malformed" ? "{}" : "{\"choices\":[{\"message\":{\"content\":\"Grilling uses direct heat.\"}}]}")
            });
        }
    }
}
