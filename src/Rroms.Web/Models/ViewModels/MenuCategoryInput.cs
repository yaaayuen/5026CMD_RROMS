using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for creating or editing a menu category (WL-1).
/// </summary>
public class MenuCategoryInput
{
    /// <summary>Category name. Must be unique.</summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Category name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Position on the menu; lower numbers first.</summary>
    [Range(MenuCategory.MinDisplayOrder, MenuCategory.MaxDisplayOrder)]
    [Display(Name = "Display order")]
    public int DisplayOrder { get; set; }

    /// <summary>False hides the category from the customer menu.</summary>
    [Display(Name = "Shown on menu")]
    public bool IsActive { get; set; } = true;
}
