using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// A date range when the restaurant is closed even though it would normally open,
/// for example 1-3 February for Chinese New Year (WL-1).
/// </summary>
/// <remarks>
/// Closures affect which bookings are possible, so they belong to restaurant setup (WL).
/// Alex may still publish an announcement about a closure, but the booking rule lives here.
/// Dates are Malaysia calendar dates and both ends are inclusive.
/// </remarks>
public class SpecialClosure
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>First closed day (inclusive).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last closed day (inclusive). Must not be before <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Reason shown to staff and customers, e.g. "Chinese New Year".</summary>
    [Required]
    [StringLength(200)]
    public string Reason { get; set; } = string.Empty;
}
