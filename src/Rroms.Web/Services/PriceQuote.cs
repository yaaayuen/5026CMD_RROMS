namespace Rroms.Web.Services;

/// <summary>The answer to "what does this dish cost right now, and why?". Owner: YJ.</summary>
public sealed class PriceQuote
{
    /// <summary>The normal price.</summary>
    public decimal NormalPrice { get; init; }

    /// <summary>The price the customer pays right now.</summary>
    public decimal Price { get; init; }

    /// <summary>True when the price comes from the dish's own Offer or from a discount.</summary>
    public bool IsOffer { get; init; }

    /// <summary>Text for the badge when a discount set the price, for example "Weekday lunch · 15% off". Null for no offer or the dish's own Offer.</summary>
    public string? Label { get; init; }

    /// <summary>Text for the small badge: "Offer" for the dish's own offer, "15% off" for a discount. Null when there is no offer.</summary>
    public string? Badge { get; init; }

    /// <summary>A line under the price: the discount name and end date, or why the dish's own offer won. Null when there is nothing to say.</summary>
    public string? Note { get; init; }

    /// <summary>When the dish's own offer ends (UTC), or null when it has no end date.</summary>
    public DateTime? EndsAtUtc { get; init; }
}
