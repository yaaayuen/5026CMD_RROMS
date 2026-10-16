namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// The profile fields user can see about themselves
///Does not expose security fields such as pw hash
/// </summary>

public class ProfileDetails
{
    //Login email (readonly)
    public string Email { get; set; } = string.Empty;

    //Name shown on bookings and receipts
    public string FullName { get; set; } = string.Empty;

    //Optional contact number
    public string? PhoneNumber { get; set; }

    //User's role shown as a label
    public string RoleName { get; set; } = string.Empty;

    //Account creation time
    public DateTime CreatedAtUtc { get; set; }

    //True when email two step sign in is on
    public bool TwoStepEnabled { get; set; }

    //True when the customer has agreed to their dietary tags being stored and used 
    public bool HasDietaryConsent { get; set; }

    //IDs of the dietary tags the customer avoids
    public IReadOnlyList<int> AvoidedTagIds { get; set; } = new List<int>();
}