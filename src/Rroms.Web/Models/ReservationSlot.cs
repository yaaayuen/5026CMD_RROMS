namespace Rroms.Web.Models;

/// <summary>
/// One time slot that a reservation holds on one table (WL-2), e.g. table T03 at 19:30.
/// </summary>
/// <remarks>
/// A 120-minute visit with 30-minute slots holds four rows. The database has a unique index on
/// (DiningTableId, SlotStartUtc), so if two bookings try to hold the same table at the same time,
/// the second insert is rejected by SQL Server itself, even if both requests arrive at the same moment.
/// </remarks>
public class ReservationSlot
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the reservation that holds this slot.</summary>
    public int ReservationId { get; set; }

    /// <summary>The reservation that holds this slot.</summary>
    public Reservation? Reservation { get; set; }

    /// <summary>
    /// The table being held. Copied from the reservation on purpose: the unique index needs
    /// the table and the time in the same row.
    /// </summary>
    public int DiningTableId { get; set; }

    /// <summary>Start of this slot (UTC), always on the slot grid, e.g. 19:00, 19:30.</summary>
    public DateTime SlotStartUtc { get; set; }
}