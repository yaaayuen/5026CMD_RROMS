using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;

namespace Rroms.Web.Models;

/// <summary>A payment recorded against one completed order. Owner: Darren (DA-1).</summary>
public class Payment
{
    /// <summary>Primary key, also used as the receipt number.</summary>
    public int Id { get; set; }
    /// <summary>The order whose saved total was paid.</summary>
    public int OrderId { get; set; }
    /// <summary>The order and its original price snapshots.</summary>
    public Order? Order { get; set; }
    /// <summary>Amount in RM, copied from Order.TotalAmount by the billing service.</summary>
    public decimal Amount { get; set; }
    /// <summary>How staff received the payment, or the clearly labelled demo method.</summary>
    public PaymentMethod Method { get; set; }
    /// <summary>DA-1 creates Paid rows only; other values are reserved for later workflows.</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    /// <summary>The signed-in staff member or administrator who recorded this payment.</summary>
    [Required, StringLength(450)]
    public string StaffId { get; set; } = string.Empty;
    /// <summary>The recording staff account.</summary>
    public ApplicationUser? Staff { get; set; }
    /// <summary>When payment was recorded, in UTC.</summary>
    public DateTime PaidAtUtc { get; set; }
}
