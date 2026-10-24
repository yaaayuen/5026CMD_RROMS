using System.Security.Cryptography;
using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// Stand-in for the restaurant's bank terminal (the project has no bank account or terminal SDK).
/// It moves no money and makes up the slip numbers; the bank and merchant names are fictional.
/// Replace it with a real terminal integration by implementing <see cref="IPaymentTerminal"/>.
/// </summary>
public sealed class SimulatedPaymentTerminal : IPaymentTerminal
{
    public const string CardAcquirer = "TG Merchant Services";
    public const string WalletNetwork = "DuitNow QR";
    public const string TerminalCode = "TG-POS-01";
    public const string MerchantId = "000000THREEGRL";

    private readonly bool _allowTestDeclines;

    /// <param name="allowTestDeclines">Test mode declines work outside Production only.</param>
    public SimulatedPaymentTerminal(bool allowTestDeclines = true)
    {
        _allowTestDeclines = allowTestDeclines;
    }

    private static readonly string[] Brands = { "Visa", "Mastercard" };
    private static readonly string[] Types = { "Credit", "Debit" };
    // No 0/O or 1/I, so a code read off a slip cannot be mistyped.
    private const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <inheritdoc />
    public TerminalResult Charge(TerminalRequest request)
    {
        bool isCard = request.Method == PaymentMethod.CardTerminal;
        if ((request.SimulateDecline && _allowTestDeclines) || request.Amount <= 0)
        {
            return new TerminalResult
            {
                Approved = false,
                Acquirer = isCard ? CardAcquirer : WalletNetwork,
                TerminalId = TerminalCode
            };
        }
        if (isCard)
        {
            return new TerminalResult
            {
                Approved = true,
                ApprovalCode = RandomCode(6),
                Reference = RandomDigits(12),
                Acquirer = CardAcquirer,
                CardBrand = Brands[RandomNumberGenerator.GetInt32(Brands.Length)],
                CardType = Types[RandomNumberGenerator.GetInt32(Types.Length)],
                CardLast4 = RandomDigits(4),
                TerminalId = TerminalCode
            };
        }
        return new TerminalResult
        {
            Approved = true,
            Reference = RandomDigits(16),
            Acquirer = WalletNetwork,
            TerminalId = TerminalCode
        };
    }

    private static string RandomCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => CodeLetters[RandomNumberGenerator.GetInt32(CodeLetters.Length)]));

    private static string RandomDigits(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));
}
