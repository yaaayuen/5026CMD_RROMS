using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>One line in the "My orders" section of the history page (YJ-5).</summary>
public class HistoryOrderRow
{
    /// <summary>The order's Id, shown as the order number and used for the link to Order Details.</summary>
    public int Id { get; set; }

    /// <summary>When the order was placed, in UTC.</summary>
    public DateTime PlacedAtUtc { get; set; }

    /// <summary>The total in RM that was saved when the order was placed.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Where the order is in the kitchen, or Cancelled.</summary>
    public OrderStatus Status { get; set; }
}
