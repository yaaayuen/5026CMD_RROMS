using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;

namespace Rroms.Web.Models;

/// <summary>A payment recorded against one completed order. Owner: Darren (DA-1).</summary>
public class Payment
{
    // Null subtotal identifies a legacy record: never retroactively add charges.
    public decimal? Subtotal { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal ServiceTaxPercent { get; set; }
    public decimal ServiceTax { get; set; }
    public bool TaxIncludesServiceCharge { get; set; }

    /// <summary>Primary key, also used as the receipt number.</summary>
    public int RefundRevision { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    [StringLength(450)] public string? VoidedById { get; set; }
    [StringLength(200)] public string? VoidReason { get; set; }
    public decimal? CashReceived { get; set; }
    public Guid? ReceiptBatchId { get; set; }
    public int Id { get; set; }
    /// <summary>The order whose saved total was paid.</summary>
    public int OrderId { get; set; }
    /// <summary>The order and its original price snapshots.</summary>
    public Order? Order { get; set; }
    /// <summary>Amount in RM, copied from Order.TotalAmount by the billing service.</summary>
    public decimal Amount { get; set; }
    /// <summary>How staff received the payment. Legacy simulated values remain readable for audit.</summary>
    public PaymentMethod Method { get; set; }
    /// <summary>Paid rows can be voided by an administrator while retaining their audit record.</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    /// <summary>The signed-in staff member or administrator who recorded this payment.</summary>
    [Required, StringLength(450)]
    public string StaffId { get; set; } = string.Empty;
    /// <summary>The recording staff account.</summary>
    public ApplicationUser? Staff { get; set; }
    /// <summary>When payment was recorded, in UTC.</summary>
    public DateTime PaidAtUtc { get; set; }
}
