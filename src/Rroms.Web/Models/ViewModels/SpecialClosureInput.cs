using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for adding a closed date range, e.g. a public holiday (WL-1).
/// </summary>
public class SpecialClosureInput
{
    /// <summary>First closed day (inclusive).</summary>
    [Display(Name = "From")]
    public DateOnly StartDate { get; set; }

    /// <summary>Last closed day (inclusive).</summary>
    [Display(Name = "To")]
    public DateOnly EndDate { get; set; }

    /// <summary>Reason shown to staff and customers.</summary>
    [Required]
    [StringLength(200)]
    public string Reason { get; set; } = string.Empty;
}
