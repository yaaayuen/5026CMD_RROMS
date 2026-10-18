using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>One line in the "My reservations" section of the history page (YJ-5).</summary>
public class HistoryReservationRow
{
    /// <summary>The reservation's Id, used for the link to its details page.</summary>
    public int Id { get; set; }

    /// <summary>When the visit starts, in UTC (the page shows it in Malaysia time).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>The table's label, for example T01.</summary>
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>The dining area's name, for example Main Hall.</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>How many guests.</summary>
    public int PartySize { get; set; }

    /// <summary>Where the reservation is in its life: pending, confirmed, seated, cancelled and so on.</summary>
    public ReservationStatus Status { get; set; }
}
