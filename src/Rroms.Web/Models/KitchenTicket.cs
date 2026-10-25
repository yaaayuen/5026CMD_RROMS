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
    /// <summary>When cooking ends. Moves if staff finish early or add time.</summary>
    public DateTime ReadyAtUtc { get; set; }
    /// <summary>When the order joined the kitchen line, kept for the average-wait statistics.</summary>
    public DateTime QueuedSinceUtc { get; set; }
    /// <summary>When staff marked the order served, kept for the ready-to-served statistics.</summary>
    public DateTime? ServedAtUtc { get; set; }
}
