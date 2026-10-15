using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Local answers use preset rules and current restaurant data.</summary>
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
        var service = Service(scope.ServiceProvider);
        var reply = await service.AskAsync(question, true);
        Assert.Equal("Local", reply.Source);
        Assert.Equal(expectedPage, reply.PageUrl);
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
        var reply = await Service(scope.ServiceProvider).AskAsync("How much is Chicken Chop?", true);
        Assert.Contains("20.00", reply.Answer);
        Assert.Equal("/Menu", reply.PageUrl);
    }
    [Fact]
    public async Task AvailabilityReadsTheBookingServiceWithoutCallingAi()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var date = Rroms.Web.Common.MalaysiaTime.FromUtc(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");
        var reply = await Service(scope.ServiceProvider).AskAsync($"table {date} 19:00 for 2", true);
        Assert.Contains("suitable tables", reply.Answer);
        Assert.Equal("/Customer/Reservations", reply.PageUrl);
    }

    [Fact]
    public async Task GuestIsSentToLoginForPersonalRecords()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider).AskAsync("my order", false);
        Assert.Equal("/Account/Login", reply.PageUrl);
    }
    [Fact]
    public async Task UnknownQuestionReturnsLocalFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var reply = await Service(scope.ServiceProvider).AskAsync("Explain grilling", true);
        Assert.Equal("Local", reply.Source);
        Assert.Null(reply.PageUrl);
        Assert.Contains("Try asking", reply.Answer);
    }
    private static ChatbotService Service(IServiceProvider services)
    {
        return new ChatbotService(services.GetRequiredService<IMenuService>(), services.GetRequiredService<IRestaurantService>(), services.GetRequiredService<IReservationService>());
    }
}
