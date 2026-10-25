namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Everything the customer menu needs to show one dish (YJ-2). A view model: it carries only what the page
/// displays, so the page contains no pricing or availability logic of its own.
/// </summary>
public class MenuItemCard
{
    /// <summary>The dish's Id, used later to add it to an order.</summary>
    public int Id { get; set; }

    /// <summary>The dish code shown to customers, for example "P01". Empty when the dish has none.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Dish name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Name of the menu category the dish is in.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Web address of the dish photo, or null when it has none.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>The normal price in RM.</summary>
    public decimal NormalPrice { get; set; }

    /// <summary>The price in RM a customer pays right now: the offer price while an offer is running.</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>True while an offer is running right now.</summary>
    public bool IsOfferActive { get; set; }

    /// <summary>Text for the small badge next to the price: "Offer", or "15% off" for a discount.</summary>
    public string OfferBadge { get; set; } = "Offer";

    /// <summary>A line under the price: the discount name and end date, or why the dish's own offer won; null when empty.</summary>
    public string? OfferNote { get; set; }

    /// <summary>Badge text when a discount set the price, for example "Weekday lunch · 15% off"; null otherwise.</summary>
    public string? OfferLabel { get; set; }

    /// <summary>When the running offer ends (UTC), or null when no offer is running.</summary>
    public DateTime? OfferEndsAtUtc { get; set; }

    /// <summary>False when the restaurant has switched ordering off for this dish.</summary>
    public bool IsAvailable { get; set; }

    /// <summary>Names of the dietary tags the dish contains, in display order (for the badges).</summary>
    public IReadOnlyList<string> DietaryTags { get; set; } = new List<string>();

    /// <summary>True when the customer may add the dish to an order. Unavailable dishes can be seen but not added.</summary>
    public bool CanOrder { get; set; }
}
