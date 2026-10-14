namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// What a customer sends to place an order (YJ-3): the reservation, the dishes and an optional note.
/// </summary>
public class PlaceOrderRequest
{
    /// <summary>The reservation the order is for. It must be the customer's own.</summary>
    public int ReservationId { get; set; }

    /// <summary>Optional note for the whole order, e.g. "Allergic to peanuts".</summary>
    public string? Notes { get; set; }

    /// <summary>The dishes ordered. The same dish may appear twice; the service merges the lines.</summary>
    public List<PlaceOrderLine> Lines { get; set; } = new List<PlaceOrderLine>();
}