using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>Card / e-wallet terminal details shown on the receipt and on the terminal slip copies.</summary>
public sealed class TerminalSlip
{
    public PaymentMethod Method { get; init; }
    public string? ApprovalCode { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Acquirer { get; init; } = string.Empty;
    public string? CardBrand { get; init; }
    public string? CardType { get; init; }
    public string? CardLast4 { get; init; }
    public string TerminalId { get; init; } = string.Empty;

    public bool IsCard => Method == PaymentMethod.CardTerminal;
    /// <summary>For example "Visa Debit **** 4821".</summary>
    public string CardText => $"{CardBrand} {CardType} **** {CardLast4}".Trim();
    /// <summary>Card slips call it RRN; e-wallet slips call it Transaction ID.</summary>
    public string ReferenceLabel => IsCard ? "Reference no. (RRN)" : "Transaction ID";

    /// <summary>Reads the slip saved on a payment; null when it has none (cash or older payments).</summary>
    public static TerminalSlip? From(Payment? payment) =>
        payment?.TerminalReference == null ? null : new TerminalSlip
        {
            Method = payment.Method,
            ApprovalCode = payment.ApprovalCode,
            Reference = payment.TerminalReference,
            Acquirer = payment.Acquirer ?? string.Empty,
            CardBrand = payment.CardBrand,
            CardType = payment.CardType,
            CardLast4 = payment.CardLast4,
            TerminalId = payment.TerminalId ?? string.Empty
        };
}
