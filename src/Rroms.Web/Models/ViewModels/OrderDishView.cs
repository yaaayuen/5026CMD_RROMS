namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One dish card of the ordering view (YJ-3): the dish and what the form needs for its quantity box.
/// </summary>
public class OrderDishView
{
    /// <summary>The dish with its current price.</summary>
    public MenuItemCard Dish { get; set; } = new MenuItemCard();

    /// <summary>Row number in the form (0, 1, 2 ... over orderable dishes only, no gaps).</summary>
    public int Row { get; set; }

    /// <summary>Tags of this dish that the customer avoids (empty when none).</summary>
    public IReadOnlyList<string> Avoided { get; set; } = new List<string>();

    /// <summary>The quantity to show (what the customer typed before, or 0).</summary>
    public int Quantity { get; set; }

    /// <summary>The note to show (what the customer typed before, or empty).</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>The largest quantity of one dish.</summary>
    public int MaxQuantity { get; set; }

    /// <summary>Longest note for one dish.</summary>
    public int MaxLineNoteLength { get; set; }

    /// <summary>True on the Menu page, where each card shows an "Already ordered" badge.</summary>
    public bool ShowAlreadyOrdered { get; set; }

    /// <summary>How many of this dish the selected reservation has already ordered (0 hides the badge).</summary>
    public int AlreadyOrdered { get; set; }

    /// <summary>
    /// Already-ordered counts for every reservation that can order, as "reservationId:count|reservationId:count",
    /// so the page can change the badge when the customer switches reservation without reloading.
    /// </summary>
    public string OrderedByReservation { get; set; } = string.Empty;
}
