namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One row in the administrator's account list, and the data for the edit form (YY-5)
/// </summary>

public class UserSummary
{
    //Identity user ID
    public string Id { get; set; } = string.Empty;

    //Display name
    public string FullName { get; set; } = string.Empty;

    //Login email
    public string Email { get; set; } = string.Empty;

    //Optional phone number
    public string? PhoneNumber { get; set; }

    //The user's single role, or empty if none
    public string RoleName { get; set; } = string.Empty;

    //False when the account has been deactivated
    public bool IsActive { get; set; }

    //When the account was created in UTC
    public DateTime CreatedAtUtc { get; set; }
}
