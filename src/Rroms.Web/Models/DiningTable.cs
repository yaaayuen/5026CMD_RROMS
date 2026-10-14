using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// A physical table that customers can book (WL-1).
/// </summary>
/// <remarks>
/// Tables are deactivated rather than deleted, so past reservations keep pointing at a real table.
/// A table can be booked only when both the table and its area are active.
/// </remarks>
public class DiningTable
{
    /// <summary>Smallest number of seats a table can have.</summary>
    public const int MinCapacity = 1;

    /// <summary>
    /// Largest number of seats a table can have. Matches the largest party size
    /// in ReservationRulesOptions (20), so any allowed party can fit at some table.
    /// </summary>
    public const int MaxCapacity = 20;

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Label staff use to find the table, e.g. "T01". Unique across the restaurant.</summary>
    [Required]
    [StringLength(20)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Number of seats.</summary>
    [Range(MinCapacity, MaxCapacity)]
    public int Capacity { get; set; }

    /// <summary>Foreign key to the area this table belongs to.</summary>
    public int DiningAreaId { get; set; }

    /// <summary>The area this table belongs to.</summary>
    public DiningArea? DiningArea { get; set; }

    /// <summary>False when the table is out of use, e.g. being repaired. Inactive tables cannot be booked.</summary>
    public bool IsActive { get; set; } = true;
}
