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
    /// <summary>The restaurant's public answer, shown to the customer and under the review.</summary>
    [StringLength(1000)]
    public string? Reply { get; set; }
    public DateTime? RepliedAtUtc { get; set; }
    /// <summary>Hidden by an administrator: kept for the averages but never shown on the website.</summary>
    public bool IsHidden { get; set; }
    /// <summary>Hidden automatically because the comment contained a blocked word.</summary>
    public bool IsFlagged { get; set; }
    /// <summary>The comment text was deleted by an administrator. The star rating is kept.</summary>
    public bool CommentRemoved { get; set; }
    /// <summary>Stars for the dishes from this visit that the guest chose to rate.</summary>
    public List<DishRating> DishRatings { get; set; } = new List<DishRating>();
}
