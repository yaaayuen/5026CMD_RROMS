using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for creating or editing a menu item (YJ-1).
/// </summary>
/// <remarks>
/// A separate class from <see cref="MenuItem"/> 
/// so a browser can never set fields it should not control (Id, image file name, timestamps). 
/// The offer times are UTC here: the admin page converts
/// the form's Malaysia time with MalaysiaTime.ToUtc before calling the service.
/// </remarks>
public class MenuItemInput
{
    /// <summary>Dish name. Must be unique within its category.</summary>
    [Required]
    [StringLength(MenuItem.NameMaxLength)]
    [Display(Name = "Dish name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description shown to customers.</summary>
    [StringLength(MenuItem.DescriptionMaxLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The category the dish belongs to (one of WL's menu categories).</summary>
    [Display(Name = "Category")]
    public int MenuCategoryId { get; set; }

    /// <summary>Normal price in RM. Must be greater than zero.</summary>
    [Display(Name = "Price (RM)")]
    public decimal Price { get; set; }

    /// <summary>False means customers can see the dish but cannot order it.</summary>
    [Display(Name = "Available to order")]
    public bool IsAvailable { get; set; } = true;

    /// <summary>Offer price in RM. Leave empty (with the two dates) for no offer.</summary>
    [Display(Name = "Offer price (RM)")]
    public decimal? PromoPrice { get; set; }

    /// <summary>When the offer starts (UTC).</summary>
    [Display(Name = "Offer starts")]
    public DateTime? PromoStartUtc { get; set; }

    /// <summary>When the offer ends (UTC).</summary>
    [Display(Name = "Offer ends")]
    public DateTime? PromoEndUtc { get; set; }
}