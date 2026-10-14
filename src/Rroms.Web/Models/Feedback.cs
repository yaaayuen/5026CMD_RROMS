using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Models;

/// <summary>One review per completed reservation. Its customer comes from the reservation.</summary>
public class Feedback
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public Reservation? Reservation { get; set; }
    [Range(1, 5)]
    public int Rating { get; set; }
    [StringLength(1000)]
    public string Comment { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
