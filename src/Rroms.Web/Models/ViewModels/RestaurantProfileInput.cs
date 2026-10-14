using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for editing the restaurant's contact details (WL-1).
/// </summary>
/// <remarks>
/// Pages bind to this class instead of the <see cref="RestaurantProfile"/> entity, so a user can only
/// send the fields listed here (no Id, no timestamps). This prevents over-posting.
/// </remarks>
public class RestaurantProfileInput
{
    /// <summary>Restaurant name shown to customers.</summary>
    [Required]
    [StringLength(100)]
    [Display(Name = "Restaurant name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Full postal address.</summary>
    [Required]
    [StringLength(250)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Contact phone number.</summary>
    [Required]
    [Phone]
    [StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Contact email address.</summary>
    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;
}
