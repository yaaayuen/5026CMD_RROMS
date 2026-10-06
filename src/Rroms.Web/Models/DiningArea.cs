using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// A part of the restaurant that contains tables, for example "Main hall" or "Patio" (WL-1).
/// </summary>
/// <remarks>
/// Areas are deactivated rather than deleted, because their tables will be referenced by past reservations.
/// </remarks>
public class DiningArea
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Area name. Unique across the restaurant.</summary>
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional note for staff, e.g. "Smoking allowed".</summary>
    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>False when the whole area is closed. Its tables then cannot be booked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The tables in this area.</summary>
    public ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
}
