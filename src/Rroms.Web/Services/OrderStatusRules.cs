using Rroms.Web.Contracts;
namespace Rroms.Web.Services;

/// <summary>Alex's allowed order transitions. Finished orders cannot be reopened.</summary>
public static class OrderStatusRules
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Moves = new Dictionary<OrderStatus, OrderStatus[]>
    {
        [OrderStatus.Placed] = new[] { OrderStatus.Preparing, OrderStatus.Cancelled },
        [OrderStatus.Preparing] = new[] { OrderStatus.ReadyToServe, OrderStatus.Cancelled },
        [OrderStatus.ReadyToServe] = new[] { OrderStatus.Served, OrderStatus.Cancelled },
        [OrderStatus.Served] = new[] { OrderStatus.Completed }
    };

    /// <summary>Returns the buttons staff may use for the current status.</summary>
    public static OrderStatus[] Next(OrderStatus status)
    {
        return Moves.TryGetValue(status, out OrderStatus[]? next) ? next : Array.Empty<OrderStatus>();
    }

    /// <summary>Checks the order flow independently of reservation eligibility.</summary>
    public static bool CanMove(OrderStatus from, OrderStatus to)
    {
        return Next(from).Contains(to);
    }

    /// <summary>The steps a customer sees on Track my visit, in order. Cancelled is not a step.</summary>
    public static readonly OrderStatus[] Steps =
    {
        OrderStatus.Placed, OrderStatus.Preparing, OrderStatus.ReadyToServe, OrderStatus.Served, OrderStatus.Completed
    };

    /// <summary>The friendly words for a status, e.g. "Ready to serve" instead of ReadyToServe.</summary>
    public static string Label(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.ReadyToServe => "Ready to serve",
            _ => status.ToString()
        };
    }
}
