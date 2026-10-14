using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// The booking an order is placed for, with what the order page shows about it (YJ-3).
/// Only returned for a customer's own Confirmed or Seated reservation.
/// </summary>
public class OrderableReservation
{
    /// <summary>Reservation ID.</summary>
    public int Id { get; set; }

    /// <summary>Table label, e.g. "T03".</summary>
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>Area name, e.g. "Main Hall".</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>Visit start (UTC).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Number of guests.</summary>
    public int PartySize { get; set; }

    /// <summary>Confirmed (not arrived yet) or Seated.</summary>
    public ReservationStatus Status { get; set; }
}
