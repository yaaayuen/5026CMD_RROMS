namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// What happens after a correct email and password (YY-7).
/// Either the user is fully signed in (go to <see cref="HomePath"/>), or two-step sign-in is on
/// and a 6-digit code has been emailed (<see cref="RequiresCode"/>), so the login page sends them
/// to the "Enter your code" page instead.
/// </summary>
public class SignInOutcome
{
    //True when the user must still enter the emailed code
    public bool RequiresCode { get; set; }

    //The role home page, e.g. "/Customer". Empty while a code is still required
    public string HomePath { get; set; } = string.Empty;
}
