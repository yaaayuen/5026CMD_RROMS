using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Orders;

/// <summary>
/// The customer's order confirmation: dishes, prices, total and where the order is in the kitchen. Owner: YJ (YJ-3).
/// Notification links point here: /Customer/Orders/Details/{id}.
/// </summary>
/// <remarks>
/// An order that does not exist or belongs to someone else returns 404, because the service only finds
/// orders for the signed-in customer (shared-contracts.md section 3).
/// </remarks>
public class DetailsModel : PageModel
{
    private const int TogetherCount = 3;

    private readonly IBillingService _billing;
    public ReceiptSummary? PaymentReceipt { get; private set; }
    private readonly IOrderService _orderService;
    private readonly IRecommendationService _recommendationService;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="billing">Reads the payment after ownership is verified.</param>
    /// <param name="orderService">Reads the customer's orders.</param>
    /// <param name="recommendationService">Suggests dishes often ordered with these ones (YJ-4).</param>
    public DetailsModel(IOrderService orderService, IRecommendationService recommendationService, IBillingService billing)
    {
        _orderService = orderService;
        _billing = billing;
        _recommendationService = recommendationService;
    }

    /// <summary>The order being shown.</summary>
    public OrderSummary Summary { get; private set; } = new OrderSummary();

    /// <summary>"People who ordered these also ordered..." (YJ-4).</summary>
    public DishSuggestionSection Together { get; private set; } = new DishSuggestionSection();

    /// <summary>Handles GET /Customer/Orders/Details/{id}.</summary>
    /// <param name="id">The order to show.</param>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        OrderSummary? summary = await _orderService.GetOrderForCustomerAsync(User.GetUserId(), id);

        if (summary == null)
        {
            return NotFound();
        }

        Summary = summary;
        // Read payment only after the existing service has verified order ownership.
        var bill = await _billing.GetBillAsync(id);
        if(bill?.PaidPaymentId is int paymentId)
            PaymentReceipt = await _billing.GetReceiptForCustomerAsync(paymentId, User.GetUserId());

        List<int> dishIds = summary.Lines.Select(line => line.MenuItemId).ToList();

        Together = new DishSuggestionSection
        {
            Heading = "People who ordered these also ordered",
            Key = "together",
            Items = await _recommendationService.GetOftenOrderedTogetherAsync(dishIds, TogetherCount, User.GetUserId())
        };

        return Page();
    }

    /// <summary>The words the customer sees for a status.</summary>
    /// <param name="status">The order status.</param>
    public static string StatusText(OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.Placed:
                return "Placed";
            case OrderStatus.Preparing:
                return "Being prepared";
            case OrderStatus.ReadyToServe:
                return "Ready to serve";
            case OrderStatus.Served:
                return "Served";
            case OrderStatus.Completed:
                return "Completed";
            case OrderStatus.Cancelled:
                return "Cancelled";
            default:
                return status.ToString();
        }
    }

    /// <summary>The Bootstrap colour class for a status badge.</summary>
    /// <param name="status">The order status.</param>
    public static string StatusBadgeClass(OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.Placed:
                return "text-bg-primary";
            case OrderStatus.Preparing:
                return "text-bg-warning";
            case OrderStatus.ReadyToServe:
                return "text-bg-info";
            case OrderStatus.Served:
            case OrderStatus.Completed:
                return "text-bg-success";
            case OrderStatus.Cancelled:
                return "text-bg-danger";
            default:
                return "text-bg-secondary";
        }
    }
}
