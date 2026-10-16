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

    //False when an administrator suspends (deactivates) the account. Suspended users cannot sign in.
    //Deactivate instead of deleting so past orders and payments keep their owner.
    public bool IsActive { get; set; } = true;

    // The account creation time, in UTC.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    //When the customer agreed that their dietary tags may be stored and used to filter recommendations (YY-9).
    //Null means no consent, so no tags are stored.
    public DateTime? DietaryConsentAtUtc { get; set; }

    //Things the customer avoids, e.g. Dairy or Spicy (YY-9). Customers only.
    public ICollection<DietaryTag> AvoidedDietaryTags { get; set; } = new List<DietaryTag>();
}