namespace Rroms.Web.Models;

/// <summary>What a seated guest asked for from the tracking page.</summary>
public enum TableRequestKind
{
    CallStaff,
    Bill
}

/// <summary>A seated guest asking for a staff member or the bill (Alex). Open until staff mark it done.</summary>
public class TableRequest
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public Reservation? Reservation { get; set; }
    public TableRequestKind Kind { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}
