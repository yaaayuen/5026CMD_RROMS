using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>Editable payment method and cash tender. Amount and staff identity never come from the form.</summary>
public class RecordPaymentInput
{
    [Display(Name = "Cash received (RM)")]
    public decimal? CashReceived { get; set; }
    [Required(ErrorMessage = "Choose a payment method.")]
    [EnumDataType(typeof(PaymentMethod), ErrorMessage = "Choose a valid payment method.")]
    [Display(Name = "Payment method")]
    public PaymentMethod? Method { get; set; }
    /// <summary>Test mode (not in Production): makes the terminal decline, to show the failure path.</summary>
    public bool SimulateDecline { get; set; }
}
