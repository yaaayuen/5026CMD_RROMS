namespace Rroms.Web.Options;

/// <summary>How many orders the kitchen cooks at the same time. Later orders wait in the queue.</summary>
public class KitchenOptions
{
    public const string SectionName = "Kitchen";
    public int CookingSlots { get; set; } = 4;
}
