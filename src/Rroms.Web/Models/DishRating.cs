using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Models;

/// <summary>Optional stars for one dish the guest ate, given with the visit's feedback (Alex).</summary>
public class DishRating
{
    public int Id { get; set; }
    public int FeedbackId { get; set; }
    public Feedback? Feedback { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }
    [Range(1, 5)]
    public int Rating { get; set; }
}
