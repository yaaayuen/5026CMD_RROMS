using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>An order as shown on the customer's confirmation page (YJ-3).</summary>
public class OrderSummary
{
    public BillBreakdown Breakdown { get; set; } = new();
    /// <summary>Order ID.</summary>
    public int Id { get; set; }

    /// <summary>The reservation the order belongs to.</summary>
    public int ReservationId { get; set; }

    /// <summary>Table label, e.g. "T03".</summary>
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>Where the order is in the kitchen workflow.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>The total in RM, calculated by the server when the order was placed.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>The customer's note for the whole order, if any.</summary>
    public string? Notes { get; set; }

    /// <summary>When the order was placed (UTC).</summary>
    public DateTime PlacedAtUtc { get; set; }

    /// <summary>The dishes ordered.</summary>
    public List<OrderSummaryLine> Lines { get; set; } = new List<OrderSummaryLine>();
}
