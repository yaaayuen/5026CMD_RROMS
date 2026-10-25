using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// One refunded dish on a refund (for example "Tofu x1"). A refund without lines refunds the whole receipt.
/// The kitchen's order and its items are never changed: the refund is recorded only against the payment.
/// </summary>
public class PaymentRefundLine
{
    public int Id { get; set; }
    public int PaymentRefundId { get; set; }
    public PaymentRefund? PaymentRefund { get; set; }
    /// <summary>The ordered dish line being refunded.</summary>
    public int OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }
    /// <summary>Dish name at the time of the refund, so the receipt still reads correctly if the menu changes.</summary>
    [Required, StringLength(MenuItem.NameMaxLength)]
    public string DishName { get; set; } = string.Empty;
    /// <summary>How many of that dish are refunded (1 up to the quantity ordered).</summary>
    public int Quantity { get; set; }
    /// <summary>Refunded RM: the dish price plus its share of service charge and tax.</summary>
    public decimal Amount { get; set; }
}
