using System.ComponentModel.DataAnnotations;

using Rroms.Web.Contracts;

namespace Rroms.Web.Models;

/// <summary>
/// One table booking made by a customer (WL-2).
/// </summary>
/// <remarks>
/// Times are stored in UTC and shown in Malaysia time with MalaysiaTime.FromUtc.
/// The rows in <see cref="Slots"/> are what actually stop double booking: the database
/// refuses two slot rows for the same table and start time.
/// </remarks>
public class Reservation
{
    /// <summary>Longest note a customer can add, e.g. "Birthday, window seat please".</summary>
    public const int SpecialRequestsMaxLength = 300;

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the customer who booked (an Identity user ID, which is a string).</summary>
    [Required]
    [StringLength(450)]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>The customer who booked.</summary>
    public ApplicationUser? Customer { get; set; }

    /// <summary>Foreign key to the booked table.</summary>
    public int DiningTableId { get; set; }

    /// <summary>The booked table.</summary>
    public DiningTable? DiningTable { get; set; }

    /// <summary>Number of guests. Limits come from ReservationRulesOptions and are checked in the service.</summary>
    public int PartySize { get; set; }

    /// <summary>When the visit starts (UTC).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>When the table is free again (UTC): start plus the visit length.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Where the booking is in its life cycle. New bookings wait for staff as Pending.</summary>
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    /// <summary>Optional note from the customer.</summary>
    [StringLength(SpecialRequestsMaxLength)]
    public string? SpecialRequests { get; set; }

    /// <summary>When the booking was made (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the booking last changed (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>The 30-minute time slots this booking holds on its table.</summary>
    public ICollection<ReservationSlot> Slots { get; set; } = new List<ReservationSlot>();
}