using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Everything about one booking (WL-3): used by the customer's Details page and by
/// Darren's staff daily view, which also needs the customer's name.
/// </summary>
/// <remarks>Times stay in UTC like the database; pages show them with MalaysiaTime.FromUtc.</remarks>
public class ReservationDetails
{
    /// <summary>Reservation ID.</summary>
    public int Id { get; set; }

    /// <summary>The customer's user ID.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>The customer's full name, for staff.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>The customer's phone number, if they gave one, so staff can call about a late arrival.</summary>
    public string? CustomerPhone { get; set; }

    /// <summary>Visit start (UTC).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Visit end (UTC).</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Number of guests.</summary>
    public int PartySize { get; set; }

    /// <summary>The booked table's ID.</summary>
    public int DiningTableId { get; set; }

    /// <summary>Table label, e.g. "T03".</summary>
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>Area name, e.g. "Main Hall".</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>Current status.</summary>
    public ReservationStatus Status { get; set; }

    /// <summary>The customer's note, if any.</summary>
    public string? SpecialRequests { get; set; }

    /// <summary>When the booking was made (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// True while the visit has an order that is not cancelled and not paid yet.
    /// Filled in only by the staff daily list; the Complete button waits for payment.
    /// </summary>
    public bool HasUnpaidOrders { get; set; }
}