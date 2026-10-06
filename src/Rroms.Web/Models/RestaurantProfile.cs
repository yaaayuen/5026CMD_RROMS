using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// The restaurant's contact details (WL-1).
/// </summary>
/// <remarks>
/// There is exactly one row, created by seed data and only ever updated.
/// Other modules (for example Alex's announcement pages) read these details from here
/// instead of keeping their own copy, so the address is never stored twice.
/// </remarks>
public class RestaurantProfile
{
    /// <summary>The primary key of the single profile row.</summary>
    public const int SingletonId = 1;

    /// <summary>Primary key. Always <see cref="SingletonId"/>.</summary>
    public int Id { get; set; }

    /// <summary>Restaurant name shown to customers.</summary>
    [Required]
    [StringLength(100)]
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

    /// <summary>When an administrator last changed these details, in UTC.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
