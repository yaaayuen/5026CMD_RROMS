namespace Rroms.Web.Services.Email;

/// <summary>
/// An email waiting to be sent by the background sender (YY-6).
/// </summary>
public class EmailMessage
{
    //>Creates a message
    public EmailMessage(string toAddress, string subject, string htmlBody)
    {
        ToAddress = toAddress;
        Subject = subject;
        HtmlBody = htmlBody;
    }

    //Recipient address
    public string ToAddress { get; }

    //Subject line
    public string Subject { get; }

    //HTML body. Any user-supplied text inside it must already be HTML-encoded
    public string HtmlBody { get; }
}
