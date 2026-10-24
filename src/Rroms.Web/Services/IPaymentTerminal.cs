using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// The card / e-wallet terminal that takes the money. RROMS only asks it to charge an amount and stores the
/// reference numbers it returns, so the app never sees a full card number or CVC.
/// The project uses <see cref="SimulatedPaymentTerminal"/>; a real bank terminal can replace it without
/// changing the billing code.
/// </summary>
public interface IPaymentTerminal
{
    /// <summary>Charges the amount and returns the terminal's approval or decline.</summary>
    TerminalResult Charge(TerminalRequest request);
}

/// <summary>What the billing service sends to the terminal.</summary>
public sealed class TerminalRequest
{
    /// <summary>The saved bill total in RM.</summary>
    public decimal Amount { get; init; }
    /// <summary>Card terminal or e-wallet.</summary>
    public PaymentMethod Method { get; init; }
    /// <summary>Demo only: ask the simulated terminal to decline, to show what happens when a card fails.</summary>
    public bool SimulateDecline { get; init; }
}

/// <summary>What the terminal prints on its slip. Only masked and reference data is kept.</summary>
public sealed class TerminalResult
{
    public bool Approved { get; init; }
    /// <summary>6-character approval code from the bank (card only).</summary>
    public string? ApprovalCode { get; init; }
    /// <summary>Card: 12-digit retrieval reference number (RRN). E-wallet: transaction ID.</summary>
    public string? Reference { get; init; }
    /// <summary>The bank or network that processed the payment.</summary>
    public string Acquirer { get; init; } = string.Empty;
    /// <summary>Visa or Mastercard (card only).</summary>
    public string? CardBrand { get; init; }
    /// <summary>Credit or Debit (card only).</summary>
    public string? CardType { get; init; }
    /// <summary>Last four digits printed on the slip (card only).</summary>
    public string? CardLast4 { get; init; }
    /// <summary>The terminal's ID, printed on the slip.</summary>
    public string TerminalId { get; init; } = string.Empty;
}
