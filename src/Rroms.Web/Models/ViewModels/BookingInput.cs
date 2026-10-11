using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// The booking the customer confirms (WL-2): the search values plus the chosen table and an optional note.
/// </summary>
/// <remarks>
/// It extends <see cref="AvailabilitySearchInput"/>, so the date, time and party size are not repeated.
/// There is no customer ID here on purpose: the page passes the signed-in user's ID separately,
/// so nobody can book in someone else's name by editing the form.
/// </remarks>
public class BookingInput : AvailabilitySearchInput
{
    /// <summary>The table the customer picked from the search results.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Choose a table.")]
    public int DiningTableId { get; set; }

    /// <summary>Optional note, e.g. "Birthday, window seat please".</summary>
    [StringLength(Reservation.SpecialRequestsMaxLength)]
    [Display(Name = "Special requests (optional)")]
    public string? SpecialRequests { get; set; }
}