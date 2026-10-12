namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// What a customer typed into the menu search and filter form (YJ-2). Every field is optional.
/// </summary>
public class MenuSearchCriteria
{
    /// <summary>Words to look for in the dish name or description. 
    /// Empty means no text filter.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>Only dishes in this category, or null for every category.</summary>
    public int? MenuCategoryId { get; set; }

    /// <summary>Lowest price in RM the customer would pay today (an offer price counts), 
    /// or null for no minimum.</summary>
    public decimal? MinPrice { get; set; }

    /// <summary>Highest price in RM the customer would pay today (an offer price counts), 
    /// or null for no maximum.</summary>
    public decimal? MaxPrice { get; set; }
}