namespace Rroms.Web.Constants;
/// <summary>
/// The three users roles in the system are defined in this class.
/// </summary>

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    // Every role name is stored in a list for easy access.
    public static readonly IReadOnlyList<string> All = new[] { Administrator, Staff, Customer };
}
