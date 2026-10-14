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

    //Free-text dining preferences (customers)
    public string? DiningPreferences { get; set; }

    //User's role shown as a label
    public string RoleName { get; set; } = string.Empty;

    //Account creation time
    public DateTime CreatedAtUtc { get; set; }

    //True when email two step sign in is on
    public bool TwoStepEnabled { get; set; }
}