using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>The only editable payment field. Amount and staff identity never come from the form.</summary>
public class RecordPaymentInput
{
    [Required(ErrorMessage = "Choose a payment method.")]
    [EnumDataType(typeof(PaymentMethod), ErrorMessage = "Choose a valid payment method.")]
    [Display(Name = "Payment method")]
    public PaymentMethod? Method { get; set; }
}
