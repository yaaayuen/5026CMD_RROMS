namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One dish a customer wants to order (YJ-3). Deliberately has NO price: the server always reads the price
/// from the database, so a browser can never choose what it pays.
/// </summary>
public class PlaceOrderLine
{
    /// <summary>The dish to order.</summary>
    public int MenuItemId { get; set; }

    /// <summary>How many portions. Must be from 1 to the limit in OrderRulesOptions.</summary>
    public int Quantity { get; set; }

    /// <summary>Optional note for this dish only, e.g. "No onions".</summary>
    public string? Notes { get; set; }
}