using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.History;

/// <summary>
/// "My history": the signed-in customer's own reservations, orders and payments, newest first. Owner: YJ (YJ-5).
/// URL: /Customer/History.
/// </summary>
/// <remarks>
/// Read only. The Customer area folder is already limited to customers (Program.cs), and the service filters by the
/// signed-in customer's Id, so this page can only ever show its own user's records.
/// </remarks>
public class IndexModel : PageModel
{
    private const string TimeFormat = "d MMM yyyy, h:mm tt";

    private readonly ICustomerHistoryService _historyService;

    /// <summary>Receives the history service from dependency injection.</summary>
    /// <param name="historyService">Reads the customer's own records.</param>
    public IndexModel(ICustomerHistoryService historyService)
    {
        _historyService = historyService;
    }

    /// <summary>The records shown on the page.</summary>
    public CustomerHistory History { get; private set; } = new CustomerHistory();

    /// <summary>How many rows each section shows at most.</summary>
    public int RowLimit
    {
        get { return ICustomerHistoryService.DefaultTake; }
    }

    /// <summary>Handles GET /Customer/History.</summary>
    public async Task OnGetAsync()
    {
        History = await _historyService.GetHistoryAsync(User.GetUserId());
    }

    /// <summary>Shows a UTC time in Malaysia time (the database keeps UTC).</summary>
    /// <param name="utc">The time in UTC.</param>
    public static string FormatTime(DateTime utc)
    {
        return MalaysiaTime.FromUtc(utc).ToString(TimeFormat);
    }

    /// <summary>The words the customer sees for how a payment was made.</summary>
    /// <param name="method">The payment method.</param>
    public static string MethodText(PaymentMethod method)
    {
        switch (method)
        {
            case PaymentMethod.Cash:
                return "Cash";
            case PaymentMethod.CardTerminal:
                return "Card";
            case PaymentMethod.EWallet:
                return "E-wallet";
            case PaymentMethod.SimulatedOnline:
                return "Online (demo)";
            default:
                return method.ToString();
        }
    }

    /// <summary>The shared status pill class for a reservation status (same colours as every other page).</summary>
    /// <param name="status">The reservation status.</param>
    public static string ReservationBadgeClass(ReservationStatus status)
    {
        return Rroms.Web.Common.ReservationStatusDisplay.BadgeClass(status);
    }
}
