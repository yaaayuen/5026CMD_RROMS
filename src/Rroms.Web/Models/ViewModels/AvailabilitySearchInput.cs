using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// What the customer asks for when looking for a table (WL-2): a date, a start time and a party size.
/// </summary>
/// <remarks>
/// Date and time are Malaysia local time, exactly as the customer picked them. The service converts them to UTC.
/// Party size limits come from ReservationRulesOptions, so they are checked in the service, not with [Range].
/// </remarks>
public class AvailabilitySearchInput
{
    /// <summary>The day of the visit (Malaysia).</summary>
    [Required]
    [Display(Name = "Date")]
    public DateOnly Date { get; set; }

    /// <summary>The start time of the visit (Malaysia), on the booking grid, e.g. 19:00 or 19:30.</summary>
    [Required]
    [Display(Name = "Time")]
    public TimeOnly Time { get; set; }

    /// <summary>Number of guests, including the customer.</summary>
    [Display(Name = "Guests")]
    public int PartySize { get; set; } = 2;
}