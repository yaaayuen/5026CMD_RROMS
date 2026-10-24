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

    // Terminal slip details (card / e-wallet only). Kept on the first payment of a receipt, like CashReceived.
    // Only masked and reference data is stored: never a full card number or CVC.
    /// <summary>Card approval code printed by the terminal.</summary>
    [StringLength(10)] public string? ApprovalCode { get; set; }
    /// <summary>Card RRN or e-wallet transaction ID printed by the terminal.</summary>
    [StringLength(30)] public string? TerminalReference { get; set; }
    /// <summary>Bank or network that processed the payment.</summary>
    [StringLength(50)] public string? Acquirer { get; set; }
    /// <summary>Visa or Mastercard.</summary>
    [StringLength(20)] public string? CardBrand { get; set; }
    /// <summary>Credit or Debit.</summary>
    [StringLength(10)] public string? CardType { get; set; }
    /// <summary>Last four card digits shown on the slip.</summary>
    [StringLength(4)] public string? CardLast4 { get; set; }
    /// <summary>The terminal that took the payment.</summary>
    [StringLength(20)] public string? TerminalId { get; set; }
}
