namespace Rroms.Web.Services;

/// <summary>
/// The data sent to the browser for a live notification. Kept small: the inbox page loads the rest.
/// SignalR serialises it to JSON with camelCase names (title, message, linkUrl, type).
/// </summary>
public class NotificationPushPayload
{
    //Short headline
    public string Title { get; set; } = string.Empty;

    //Message text
    public string Message { get; set; } = string.Empty;

    //Optional relative link
    public string? LinkUrl { get; set; }

    //Category name, e.g. "ReservationConfirmed"
    public string Type { get; set; } = string.Empty;
}
