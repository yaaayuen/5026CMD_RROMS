namespace Rroms.Web.Models.ViewModels;

/// <summary>The automatic kitchen at one moment: what is cooking, what waits, and what staff still need to serve.</summary>
public class KitchenBoard
{
    public DateTime NowUtc { get; set; }
    public int CookingSlots { get; set; }
    /// <summary>Orders on the stove, finishing soonest first.</summary>
    public List<KitchenEntry> Cooking { get; set; } = new List<KitchenEntry>();
    /// <summary>Orders waiting for a free slot, first in line first, with an estimated start time.</summary>
    public List<KitchenEntry> Queue { get; set; } = new List<KitchenEntry>();
    /// <summary>Cooked orders waiting for staff to take them to the table.</summary>
    public List<KitchenEntry> Ready { get; set; } = new List<KitchenEntry>();
    /// <summary>Orders at the table, waiting to be marked completed.</summary>
    public List<KitchenEntry> Served { get; set; } = new List<KitchenEntry>();

    /// <summary>The entry for one order wherever it is on the board, or null.</summary>
    public KitchenEntry? Find(int orderId) =>
        Cooking.Concat(Queue).Concat(Ready).Concat(Served).FirstOrDefault(e => e.Order.Id == orderId);
}

/// <summary>One order on the kitchen board.</summary>
public class KitchenEntry
{
    public required Order Order { get; set; }
    /// <summary>Cooking time of the order: its longest dish. Zero when it only has drinks.</summary>
    public int CookingMinutes { get; set; }
    /// <summary>When the order joined the kitchen line: when it was placed, or when the visit began for a pre-order.</summary>
    public DateTime WaitingSinceUtc { get; set; }
    /// <summary>Queue: estimated start. Cooking: when it started.</summary>
    public DateTime? StartUtc { get; set; }
    /// <summary>Queue: estimated finish. Cooking: when the countdown ends.</summary>
    public DateTime? ReadyAtUtc { get; set; }
    /// <summary>Queue only: 1 for the next order to cook.</summary>
    public int QueuePosition { get; set; }
    public string TableLabel => Order.Reservation?.DiningTable?.Label ?? "?";
}
