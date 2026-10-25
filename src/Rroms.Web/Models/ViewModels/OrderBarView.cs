namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// What the shared order bar, drawer and dish dialog need to know (YJ-3). Used by the order page and the Menu ordering view.
/// </summary>
public class OrderBarView
{
    /// <summary>Where "Cancel" goes, or null for no Cancel link (the Menu page has "Stop ordering" instead).</summary>
    public string? CancelUrl { get; set; }

    /// <summary>"Table T03 · Fri 30 Oct, 7:00 PM" shown as "Adding to: ..." above the bar, or null to show nothing.</summary>
    public string? AddingTo { get; set; }

    /// <summary>Longest note for the whole order.</summary>
    public int MaxOrderNoteLength { get; set; }

    /// <summary>The order note the customer typed before (shown again when an order is refused).</summary>
    public string? Notes { get; set; }
}
