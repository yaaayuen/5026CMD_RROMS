using System.ComponentModel.DataAnnotations;

using Rroms.Web.Common;
using Rroms.Web.Services;

namespace Rroms.Web.Models.ViewModels;

/// <summary>What the administrator types on the Discounts form. Owner: YJ.</summary>
/// <remarks>Dates are Malaysia calendar dates; the last day is INCLUDED. The service turns them into UTC moments.</remarks>
public class DiscountInput
{
    /// <summary>0 when creating a new discount.</summary>
    public int Id { get; set; }

    /// <summary>Name shown to customers.</summary>
    [Required, StringLength(MenuDiscount.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Percent off, from 1 to 90.</summary>
    [Range(MenuDiscount.MinPercent, MenuDiscount.MaxPercent)]
    public decimal Percent { get; set; }

    /// <summary>Which dishes it covers.</summary>
    public DiscountScope Scope { get; set; } = DiscountScope.WholeMenu;

    /// <summary>The categories, when Scope is Category.</summary>
    public List<int> CategoryIds { get; set; } = new List<int>();

    /// <summary>The dishes, when Scope is Dishes.</summary>
    public List<int> DishIds { get; set; } = new List<int>();

    /// <summary>First day it runs (Malaysia date), or empty for "from now".</summary>
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    /// <summary>Last day it runs (Malaysia date, included), or empty for "until switched off".</summary>
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    /// <summary>The bit values of the ticked days (see <see cref="DiscountDays"/>). None ticked means every day.</summary>
    public List<int> SelectedDays { get; set; } = new List<int>();

    /// <summary>Daily window start (Malaysia time), or empty for all day.</summary>
    public TimeOnly? FromTime { get; set; }

    /// <summary>Daily window end (Malaysia time, excluded), or empty for all day.</summary>
    public TimeOnly? ToTime { get; set; }

    /// <summary>The on/off switch.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Fills the form from a saved discount.</summary>
    /// <param name="discount">The discount (dishes loaded).</param>
    public static DiscountInput FromDiscount(MenuDiscount discount)
    {
        DiscountInput input = new DiscountInput
        {
            Id = discount.Id,
            Name = discount.Name,
            Percent = discount.Percent,
            Scope = discount.Scope,
            CategoryIds = discount.Categories.Select(category => category.Id).ToList(),
            DishIds = discount.Dishes.Select(dish => dish.Id).ToList(),
            IsActive = discount.IsActive
        };

        if (discount.StartUtc.HasValue)
        {
            input.StartDate = MalaysiaTime.FromUtc(discount.StartUtc.Value).Date;
        }

        if (discount.EndUtc.HasValue)
        {
            input.EndDate = MalaysiaTime.FromUtc(discount.EndUtc.Value).AddDays(-1).Date;
        }

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (discount.DaysOfWeek != 0 && (discount.DaysOfWeek & DiscountDays.Bit(day)) != 0)
            {
                input.SelectedDays.Add(DiscountDays.Bit(day));
            }
        }

        if (discount.FromMinute.HasValue && discount.ToMinute.HasValue)
        {
            input.FromTime = new TimeOnly(discount.FromMinute.Value / 60, discount.FromMinute.Value % 60);
            input.ToTime = discount.ToMinute.Value >= 1440
                ? new TimeOnly(23, 59)
                : new TimeOnly(discount.ToMinute.Value / 60, discount.ToMinute.Value % 60);
        }

        return input;
    }
}
