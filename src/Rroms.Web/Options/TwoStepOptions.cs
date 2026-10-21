namespace Rroms.Web.Options;

/// <summary>
/// Settings for compulsory two-step sign-in. Owner: YY (YY-9).
/// Bound from the "TwoStep" section of configuration.
/// </summary>
public class TwoStepOptions
{
    //Name of the configuration section
    public const string SectionName = "TwoStep";

    //When true, staff and administrators must use two-step sign-in. On by default, so the real
    //app is always protected. The test factory turns it off so other modules' tests can sign in
    //with a password only (the same idea as RateLimiting:PermitLimit in the tests).
    public bool RequiredForStaff { get; set; } = true;
}