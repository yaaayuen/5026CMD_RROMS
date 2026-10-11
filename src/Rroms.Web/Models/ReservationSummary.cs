using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One row in a customer's "My reservations" list (WL-2).
/// </summary>
/// <remarks>
/// Times stay in UTC like the database; the page shows them with MalaysiaTime.FromUtc.
/// </remarks>
public class ReservationSummary
{
    /// <summary>Reservation ID.</summary>
    public int Id { get; set; }

    /// <summary>Visit start (UTC).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Visit end (UTC).</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Number of guests.</summary>
    public int PartySize { get; set; }

    /// <summary>Table label, e.g. "T03".</summary>
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>Area name, e.g. "Main Hall".</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>Current status, e.g. Pending or Confirmed.</summary>
    public ReservationStatus Status { get; set; }

    /// <summary>The customer's note, if any.</summary>
    public string? SpecialRequests { get; set; }
}