namespace Rroms.Web.Options;

/// <summary>
/// Ordering rules, bound from the "OrderRules" configuration section (YJ-3).
/// Keeping the number here means a rule change is a one-line configuration edit, not a hunt through the code.
/// </summary>
public class OrderRulesOptions
{
    /// <summary>Name of the configuration section this class is bound to.</summary>
    public const string SectionName = "OrderRules";

    /// <summary>Most portions of one dish a customer can order in a single order.</summary>
    public int MaxQuantityPerLine { get; set; } = 20;
}