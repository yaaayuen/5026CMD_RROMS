using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Discounts;

/// <summary>
/// Promotions list: every discount with what it covers, when it runs and whether it is on, plus
/// switch-on/off and delete buttons. Owner: YJ.
/// </summary>
/// <remarks>/Admin/Discounts. Only administrators can open it: the whole Admin area is protected in Program.cs.</remarks>
public class IndexModel : PageModel
{
    private readonly IDiscountService _discountService;

    /// <summary>Receives the service from dependency injection.</summary>
    /// <param name="discountService">The discount service.</param>
    public IndexModel(IDiscountService discountService)
    {
        _discountService = discountService;
    }

    /// <summary>The discounts to show.</summary>
    public IReadOnlyList<MenuDiscount> Discounts { get; private set; } = new List<MenuDiscount>();

    /// <summary>Handles GET /Admin/Discounts.</summary>
    public async Task OnGetAsync()
    {
        Discounts = await _discountService.GetDiscountsAsync();
    }

    /// <summary>Handles the on/off button.</summary>
    /// <param name="id">The discount.</param>
    /// <param name="makeActive">True to switch on.</param>
    public async Task<IActionResult> OnPostToggleAsync(int id, bool makeActive)
    {
        ServiceResult result = await _discountService.SetActiveAsync(id, makeActive);
        Report(result, makeActive ? "Promotion switched on." : "Promotion switched off.");

        return RedirectToPage();
    }

    /// <summary>Handles the Delete button.</summary>
    /// <param name="id">The discount.</param>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        ServiceResult result = await _discountService.DeleteDiscountAsync(id);
        Report(result, "Promotion deleted.");

        return RedirectToPage();
    }

    /// <summary>What the discount covers, in words.</summary>
    /// <param name="discount">The discount.</param>
    public string DescribeScope(MenuDiscount discount)
    {
        return DiscountText.AppliesTo(discount);
    }

    /// <summary>The date range, or "Always" when there is none.</summary>
    /// <param name="discount">The discount.</param>
    public string DescribeDates(MenuDiscount discount)
    {
        return DiscountText.DateRange(discount) ?? "Always";
    }

    /// <summary>Days and hours, in words.</summary>
    /// <param name="discount">The discount.</param>
    public string DescribeSchedule(MenuDiscount discount)
    {
        return DiscountText.Schedule(discount);
    }

    /// <summary>The status text: Off, Ended, Starts in N days, Running now, or On, not running now.</summary>
    /// <param name="discount">The discount.</param>
    public string StatusText(MenuDiscount discount)
    {
        DateTime now = DateTime.UtcNow;

        if (!discount.IsActive)
        {
            return "Off";
        }

        if (discount.EndUtc.HasValue && now >= discount.EndUtc.Value)
        {
            return "Ended";
        }

        if (discount.StartUtc.HasValue && now < discount.StartUtc.Value)
        {
            int days = (int)Math.Ceiling((discount.StartUtc.Value - now).TotalDays);

            return days <= 1 ? "Starts tomorrow" : $"Starts in {days} days";
        }

        return DiscountRules.IsRunning(discount, now) ? "Running now" : "On, not running now";
    }

    /// <summary>The colour of the status badge.</summary>
    /// <param name="status">The text from <see cref="StatusText"/>.</param>
    public string StatusClass(string status)
    {
        if (status == "Running now")
        {
            return "text-bg-success";
        }

        if (status.StartsWith("Starts"))
        {
            return "text-bg-warning";
        }

        return status.StartsWith("On,") ? "text-bg-info" : "text-bg-secondary";
    }

    private void Report(ServiceResult result, string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = successMessage;
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }
    }
}
