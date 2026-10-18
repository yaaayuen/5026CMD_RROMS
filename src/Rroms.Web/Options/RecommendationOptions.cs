namespace Rroms.Web.Options;

/// <summary>
/// Settings for the recommendation engine, bound from the "Recommendations" configuration section (YJ-4).
/// Every number that shapes a recommendation lives here, so a change (for example 7 days instead of 30) is a
/// one-line edit and the scoring rules can be explained and tuned in one place.
/// </summary>
public class RecommendationOptions
{
    /// <summary>Name of the configuration section this class is bound to.</summary>
    public const string SectionName = "Recommendations";

    /// <summary>How many days back "popular" looks. Older orders are ignored.</summary>
    public int PopularityWindowDays { get; set; } = 30;

    /// <summary>How many dishes the pages show in a recommendation strip.</summary>
    public int DefaultCount { get; set; } = 4;

    /// <summary>
    /// Points for a dish in a category the customer often orders from. Multiplied by the customer's share of
    /// portions ordered in that category (0 to 1), so a category they always order from earns the full amount.
    /// </summary>
    public double CategoryAffinityWeight { get; set; } = 2.0;

    /// <summary>
    /// Points for being popular with everyone. Multiplied by how the dish compares with the most popular dish
    /// (0 to 1), so the most popular dish earns the full amount.
    /// </summary>
    public double PopularityWeight { get; set; } = 0.5;
}
