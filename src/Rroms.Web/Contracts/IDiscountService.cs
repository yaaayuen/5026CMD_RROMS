using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Contracts;

/// <summary>
/// Administrator tools for menu discounts. Owner: YJ.
/// Pricing itself does not use this: it reads the database through <c>DiscountSet</c>.
/// </summary>
public interface IDiscountService
{
    /// <summary>Every discount, newest first, with its category and dishes.</summary>
    Task<IReadOnlyList<MenuDiscount>> GetDiscountsAsync();

    /// <summary>One discount with its dishes, or null when it does not exist.</summary>
    /// <param name="id">The discount.</param>
    Task<MenuDiscount?> GetDiscountAsync(int id);

    /// <summary>Creates (Id 0) or updates a discount after checking every rule. Returns the Id.</summary>
    /// <param name="input">The form values.</param>
    Task<ServiceResult<int>> SaveDiscountAsync(DiscountInput input);

    /// <summary>Switches a discount on or off.</summary>
    /// <param name="id">The discount.</param>
    /// <param name="isActive">True to switch on.</param>
    Task<ServiceResult> SetActiveAsync(int id, bool isActive);

    /// <summary>Deletes a discount. Prices go back to normal at once; placed orders keep the price they were charged.</summary>
    /// <param name="id">The discount.</param>
    Task<ServiceResult> DeleteDiscountAsync(int id);

    /// <summary>One sentence for each discount running right now, for the banner on the customer menu.</summary>
    Task<IReadOnlyList<string>> GetRunningBannersAsync();
}
