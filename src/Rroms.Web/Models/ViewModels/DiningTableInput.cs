using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for creating or editing a table (WL-1).
/// </summary>
public class DiningTableInput
{
    /// <summary>Table label, e.g. "T07". Must be unique.</summary>
    [Required]
    [StringLength(20)]
    [Display(Name = "Table label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Number of seats.</summary>
    [Range(DiningTable.MinCapacity, DiningTable.MaxCapacity)]
    [Display(Name = "Seats")]
    public int Capacity { get; set; } = 2;

    /// <summary>The area the table belongs to.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Choose a dining area.")]
    [Display(Name = "Dining area")]
    public int DiningAreaId { get; set; }

    /// <summary>False takes the table out of use.</summary>
    [Display(Name = "Available for bookings")]
    public bool IsActive { get; set; } = true;
}
