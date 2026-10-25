using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>What a discount covers.</summary>
public enum DiscountScope
{
    /// <summary>Every dish on the menu.</summary>
    WholeMenu,

    /// <summary>Every dish in one or more menu categories.</summary>
    Category,

    /// <summary>Only the dishes the admin ticked.</summary>
    Dishes
}

/// <summary>
/// A percentage discount set by the administrator, for example "Weekday lunch, 15% off, Mon-Fri 11:00-15:00".
/// Owner: YJ.
/// </summary>
/// <remarks>
/// Discounts are ADDED to the dish's own Offer, never stacked: the customer always pays the cheapest price.
/// Dates, days and hours are all in Malaysia time. See <c>MenuItemPricing.Quote</c>.
/// </remarks>
public class MenuDiscount
{
    /// <summary>Longest name.</summary>
    public const int NameMaxLength = 80;

    /// <summary>Smallest percent the administrator can enter.</summary>
    public const int MinPercent = 1;

    /// <summary>Largest percent the administrator can enter.</summary>
    public const int MaxPercent = 90;

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Name shown to customers, for example "Weekday lunch".</summary>
    [Required, StringLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Percent taken off the normal price, from 1 to 90.</summary>
    /// <remarks>Fixed at 90 so a typing mistake cannot give a dish away for free.</remarks>
    public decimal Percent { get; set; }

    /// <summary>Which dishes it covers.</summary>
    public DiscountScope Scope { get; set; }

    /// <summary>The categories, when <see cref="Scope"/> is Category (one or more).</summary>
    public ICollection<MenuCategory> Categories { get; set; } = new List<MenuCategory>();

    /// <summary>The chosen dishes, when <see cref="Scope"/> is Dishes.</summary>
    public ICollection<MenuItem> Dishes { get; set; } = new List<MenuItem>();

    /// <summary>First moment it runs (UTC, inclusive), or null for "already running".</summary>
    public DateTime? StartUtc { get; set; }

    /// <summary>First moment it no longer runs (UTC, exclusive), or null for "until switched off".</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Days it runs on, one bit per day (see <c>DiscountDays</c>). 0 means every day.</summary>
    public int DaysOfWeek { get; set; }

    /// <summary>Start of the daily window as minutes after midnight in Malaysia (inclusive), or null for all day.</summary>
    public int? FromMinute { get; set; }

    /// <summary>End of the daily window as minutes after midnight in Malaysia (exclusive), or null for all day.</summary>
    public int? ToMinute { get; set; }

    /// <summary>The on/off switch.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the discount was created (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
