namespace Rroms.Web.Models.ViewModels;

/// <summary>A saved payment and whether its subsequent confirmation notification succeeded.</summary>
public class PaymentRecordingResult
{
    public int PaymentId { get; set; }
    public bool NotificationSent { get; set; }
}
