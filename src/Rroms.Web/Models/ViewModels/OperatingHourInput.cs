using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Form data for changing the opening hours of one day (WL-1).
/// </summary>
public class OperatingHourInput
{
    /// <summary>The day being changed.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Opening time, Malaysia local time.</summary>
    [Display(Name = "Opens at")]
    public TimeOnly OpensAt { get; set; }

    /// <summary>Closing time, Malaysia local time.</summary>
    [Display(Name = "Closes at")]
    public TimeOnly ClosesAt { get; set; }

    /// <summary>True when the restaurant does not open on this day.</summary>
    [Display(Name = "Closed all day")]
    public bool IsClosed { get; set; }
}
