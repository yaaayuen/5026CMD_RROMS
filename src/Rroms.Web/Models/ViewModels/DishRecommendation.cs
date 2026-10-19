namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One recommended dish together with the reason it was recommended (YJ-4).
/// Showing the reason keeps the engine explainable: every suggestion can say why it is there.
/// </summary>
public class DishRecommendation
{
    /// <summary>The dish, ready to display (current price, offer, tags).</summary>
    public MenuItemCard Dish { get; set; } = new MenuItemCard();

    /// <summary>One sentence explaining the suggestion, e.g. "Ordered 12 times in the last 30 days".</summary>
    public string Reason { get; set; } = string.Empty;
}
