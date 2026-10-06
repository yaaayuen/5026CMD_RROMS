namespace Rroms.Web.Options;

/// <summary>
/// The first administrator account, created automatically when the app starts.
/// Bound from the "BootstrapAdmin" configuration section.
/// </summary>
public class BootstrapAdminOptions
{
    //Name of the configuration section this class is bound to.
    public const string SectionName = "BootstrapAdmin";

    //Login email of the first administrator.
    public string Email { get; set; } = string.Empty;

    //Initial password. Must satisfy the Identity password policy in Program.cs.
    public string Password { get; set; } = string.Empty;
}