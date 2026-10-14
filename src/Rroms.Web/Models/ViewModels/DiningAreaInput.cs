using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for creating or editing a dining area (WL-1).
/// </summary>
public class DiningAreaInput
{
    /// <summary>Area name. Must be unique.</summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Area name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional note for staff.</summary>
    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>False closes the whole area for bookings.</summary>
    [Display(Name = "Open for bookings")]
    public bool IsActive { get; set; } = true;
}
