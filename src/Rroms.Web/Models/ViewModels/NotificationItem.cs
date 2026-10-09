using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// A notification as shown in the inbox page (YY-6)
/// </summary>

public class NotificationItem
{
    //Notification ID
    public int Id { get; set; }

    //Category
    public NotificationType Type { get; set; }

    //Short headline
    public string Title { get; set; } = string.Empty;

    //Full message
    public string Message { get; set; } = string.Empty;

    //Optional relative link
    public string? LinkUrl { get; set; }

    //True once opened
    public bool IsRead { get; set; }

    //When it was created in UTC
    public DateTime CreatedAtUtc { get; set; }
}
