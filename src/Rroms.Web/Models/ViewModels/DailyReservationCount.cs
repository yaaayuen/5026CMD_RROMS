namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One day of the reservation trend (WL-4): how many bookings started that day, split by status.
/// </summary>
public class DailyReservationCount
{
    /// <summary>The day (Malaysia date).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Bookings still waiting for staff.</summary>
    public int Pending { get; set; }

    /// <summary>Bookings confirmed but guests not arrived yet.</summary>
    public int Confirmed { get; set; }

    /// <summary>Bookings whose guests are at the table now.</summary>
    public int Seated { get; set; }

    /// <summary>Bookings whose guests have eaten and left.</summary>
    public int Completed { get; set; }

    /// <summary>Bookings cancelled by the customer or staff.</summary>
    public int Cancelled { get; set; }

    /// <summary>Bookings where the guests never came.</summary>
    public int NoShow { get; set; }

    /// <summary>All bookings that day, whatever their status.</summary>
    public int Total => Pending + Confirmed + Seated + Completed + Cancelled + NoShow;

    /// <summary>
    /// Bookings whose visit has not finished yet (Pending, Confirmed or Seated). Past days are always 0 here,
    /// because every past visit ends Completed, Cancelled or Absent; only today and later can have some.
    /// </summary>
    public int NotFinished => Pending + Confirmed + Seated;

    /// <summary>Guests who really dined (Seated or Completed bookings).</summary>
    public int GuestsServed { get; set; }
}
