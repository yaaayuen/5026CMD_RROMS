namespace Rroms.Web.Models.ViewModels;

/// <summary>One line of an order as shown on the confirmation page (YJ-3).</summary>
public class OrderSummaryLine
{
    /// <summary>The dish's Id, used to find dishes ordered together with it.</summary>
    public int MenuItemId { get; set; }

    /// <summary>The dish name.</summary>
    public string DishName { get; set; } = string.Empty;

    /// <summary>How many portions.</summary>
    public int Quantity { get; set; }

    /// <summary>The price in RM paid for one portion when the order was placed.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Quantity times the unit price.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>The customer's note for this dish, if any.</summary>
    public string? Notes { get; set; }
}
