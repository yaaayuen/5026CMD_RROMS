using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;

namespace Rroms.Web.Models;

/// <summary>
/// A person who can sign in: administrator, staff member or customer.
/// </summary>
public class ApplicationUser : IdentityUser
{
    //Name shown on bookings, orders and receipts.
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;


    //Free-text dining preferences (e.g. "window seat, no peanuts").
    //Shown to staff with reservations and used by the recommendation engine.

    [StringLength(500)]
    public string? DiningPreferences { get; set; }


    //False when an administrator deactivates the account. Inactive users cannot sign in.
    //Deactivate instead of deleting so past orders and payments keep their owner.
    public bool IsActive { get; set; } = true;

    // The account creation time, in UTC.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}