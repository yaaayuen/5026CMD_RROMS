using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// A dish or drink on the Three Grills menu. Owned by YJ (Sprint 3).
/// Rules that compare several fields (for example, promo price below normal price)
/// live in IMenuService, not here, so every failure comes back as a ServiceResult.
/// </summary>
public class MenuItem
{
    /// <summary>Maximum length of the item name.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Maximum length of the item description.</summary>
    public const int DescriptionMaxLength = 500;

    /// <summary>Maximum length of the dish code (a letter plus a number, for example "S01").</summary>
    public const int CodeMaxLength = 10;

    /// <summary>Maximum length of the stored image file name.</summary>
    public const int ImageFileNameMaxLength = 100;

    // Range attributes need string bounds for decimals.
    private const string MinPriceText = "0.01";
    private const string MaxPriceText = "99999999.99";

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Short code for the dish: its category's first letter plus a number, for example "S01" or "P02".
    /// Made by IMenuService when the dish is created and never changed after that. Null only for a dish that
    /// was saved before codes existed (the migration gives every existing dish a code).
    /// </summary>
    [StringLength(CodeMaxLength)]
    public string? Code { get; set; }

    /// <summary>Display name, for example "Smoky BBQ Burger".</summary>
    [Required]
    [StringLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description shown to customers.</summary>
    [StringLength(DescriptionMaxLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Foreign key to WL's MenuCategory.</summary>
    public int MenuCategoryId { get; set; }

    /// <summary>The category this item belongs to.</summary>
    public MenuCategory? MenuCategory { get; set; }

    /// <summary>Normal price in RM. Must be greater than zero. Stored as decimal(10,2) by the global convention.</summary>
    [Range(typeof(decimal), MinPriceText, MaxPriceText)]
    public decimal Price { get; set; }

    /// <summary>When false, customers can see the item but cannot order it.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// What the dish contains: any of YY's shared dietary tags (animal products, dairy, spicy). Shown as badges,
    /// used by the menu filter, and used so the recommendations never suggest a dish a customer avoids.
    /// </summary>
    public ICollection<DietaryTag> DietaryTags { get; set; } = new List<DietaryTag>();

    /// <summary>Promotional price in RM, or null when there is no offer.</summary>
    public decimal? PromoPrice { get; set; }

    /// <summary>When the offer starts (UTC, inclusive).</summary>
    public DateTime? PromoStartUtc { get; set; }

    /// <summary>When the offer ends (UTC, exclusive).</summary>
    public DateTime? PromoEndUtc { get; set; }

    /// <summary>
    /// Stored image file name only (a generated GUID plus extension), never a full path
    /// and never the name the user uploaded.
    /// </summary>
    [StringLength(ImageFileNameMaxLength)]
    public string? ImageFileName { get; set; }

    /// <summary>When the item was created (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the item was last changed (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Longest cooking time an administrator may enter, in minutes.</summary>
    public const int MaxCookingMinutes = 180;

    /// <summary>
    /// How long the kitchen needs to cook this dish, in minutes (Alex, kitchen countdown). Empty for drinks and
    /// anything served straight away. Not shown on the menu.
    /// </summary>
    [Range(1, MaxCookingMinutes)]
    public int? CookingMinutes { get; set; }
}
