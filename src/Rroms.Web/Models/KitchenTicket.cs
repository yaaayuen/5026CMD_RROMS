namespace Rroms.Web.Models;

/// <summary>
/// When the kitchen started cooking an order and when it will be ready (Alex, kitchen countdown).
/// Kept in its own table so the shared Order model does not change.
/// </summary>
public class KitchenTicket
{
    public int Id { get; set; }
    /// <summary>One ticket per order (unique).</summary>
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    /// <summary>The longest dish in the order, copied when cooking starts so later menu edits do not move the countdown.</summary>
    public int CookingMinutes { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime ReadyAtUtc { get; set; }
}
