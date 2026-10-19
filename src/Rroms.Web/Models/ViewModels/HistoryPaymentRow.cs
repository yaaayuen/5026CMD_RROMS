using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>One line in the "My payments" section of the history page (YJ-5). Read from Darren's payments.</summary>
public class HistoryPaymentRow
{
    /// <summary>The payment's Id, which is also the receipt number.</summary>
    public int ReceiptNumber { get; set; }

    /// <summary>When the payment was recorded, in UTC.</summary>
    public DateTime PaidAtUtc { get; set; }

    /// <summary>The amount in RM.</summary>
    public decimal Amount { get; set; }

    /// <summary>How it was paid: cash, card terminal, e-wallet or the simulated online payment.</summary>
    public PaymentMethod Method { get; set; }

    /// <summary>The order that was paid, used for the link to Order Details.</summary>
    public int OrderId { get; set; }
}
