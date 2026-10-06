using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// A section of the menu, for example "Grills" or "Beverages" (WL-1).
/// </summary>
/// <remarks>
/// WL owns the categories; YJ's menu items (YJ-1) reference them by <see cref="Id"/>.
/// Categories are deactivated rather than deleted so existing menu items keep their category.
/// </remarks>
public class MenuCategory
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Category name. Unique.</summary>
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Position on the menu; lower numbers are shown first.</summary>
    [Range(0, 999)]
    public int DisplayOrder { get; set; }

    /// <summary>False hides the category (and its items) from the customer menu.</summary>
    public bool IsActive { get; set; } = true;
}
