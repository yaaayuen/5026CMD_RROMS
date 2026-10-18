using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One titled strip of suggested dishes ("Popular this month", "Recommended for you"), shown by the shared
/// _DishSuggestions partial. Owner: YJ (YJ-4).
/// </summary>
public class DishSuggestionSection
{
    /// <summary>The heading shown above the strip.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>A short unique word used to build the heading's HTML id (for screen readers).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The suggested dishes, each with the reason it is suggested.</summary>
    public IReadOnlyList<DishRecommendation> Items { get; set; } = new List<DishRecommendation>();
}
