namespace Rroms.Web.Options;

/// <summary>
/// Gmail (SMTP) settings for notification emails, bound from the "Smtp" section (YY-6)
/// Username and Password must come from user-secrets, never appsettings.json.
/// For Gmail, Password is a 16-character "app password" created in the Google account's
/// security settings (2-Step Verification must be on), not the normal Gmail password.
/// </summary>

public class SmtpOptions
{
    //Name of the configuration section this class is bound to
    public const string SectionName = "Smtp";

    //SMTP server, e.g. smtp.gmail.com
    public string Host { get; set; } = "smtp.gmail.com";

    //587 for STARTTLS (Gmail)
    public int Port { get; set; } = 587;

    //Encrypt the connection. Always true for Gmail
    public bool EnableSsl { get; set; } = true;

    //Sender name shown in the inbox
    public string FromName { get; set; } = "Three Grills";

    //Sender address (normally the same as <see cref="Username"/> for Gmail)
    public string FromAddress { get; set; } = string.Empty;

    //SMTP login
    public string Username { get; set; } = string.Empty;

    //SMTP password (Gmail app password). Keep in user-secrets
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Public address of the website, used to turn relative links into full links in emails,
    /// e.g. "https://localhost:7184".
    /// </summary>
    public string SiteBaseUrl { get; set; } = "https://localhost:7184";

    //True when enough settings are present to send email
    public bool IsConfigured
    {
        get
        {
            return !string.IsNullOrWhiteSpace(Host)
                && !string.IsNullOrWhiteSpace(FromAddress)
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password);
        }
    }
}
