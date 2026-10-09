using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;

namespace Rroms.Web.Models;

/// <summary>
/// One message in a user's notification inbox (YY-6)
/// Stored in the database so users see notifications they missed while offline;
/// the live SignalR push is only a faster way of delivering the same record.
/// </summary>

public class Notification
{
    //Max length of title
    public const int TitleMaxLength = 100;

    //Max length of message
    public const int MessageMaxLength = 500;

    //Max length of linkurl
    public const int LinkMaxLength = 300;

    //Primary key
    public int Id { get; set; }

    //Recipient (foreign key to AspNetUsers). Only this user may see the notification
    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    //Category, used for the icon and email subject
    public NotificationType Type { get; set; }

    //Short headline, e.g. "Reservation confirmed"
    [Required]
    [StringLength(TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    //Full message text
    [Required]
    [StringLength(MessageMaxLength)]
    public string Message { get; set; } = string.Empty;

    //Optional relative link to the related page
    [StringLength(LinkMaxLength)]
    public string? LinkUrl { get; set; }

    //True once the user has opened it
    public bool IsRead { get; set; }

    //When it was created in UTC
    public DateTime CreatedAtUtc { get; set; }

    //When the user opened it in UTC
    public DateTime? ReadAtUtc { get; set; }
}
