namespace Rroms.Web.Options;

/// <summary>
/// Limits how often one IP address may submit the login and registration forms.
/// Bound from the "RateLimiting" configuration section.
/// This is a second layer of protection on top of account lockout:
/// lockout protects one account, the rate limit slows an attacker trying many accounts.
/// </summary>
public class AuthRateLimitOptions
{
    //Name of the configuration section this class is bound to.
    public const string SectionName = "RateLimiting";

    //Name of the rate-limiting policy applied to the account pages.
    public const string PolicyName = "authentication";

    //Maximum requests allowed in one window. Default: 10.
    public int PermitLimit { get; set; } = 10;

    //Length of the window in seconds. Default: 60.
    public int WindowSeconds { get; set; } = 60;
}