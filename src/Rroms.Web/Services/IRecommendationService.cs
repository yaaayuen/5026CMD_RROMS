using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Dish recommendations. Owner: YJ (Sprint 3, YJ-4).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately simple and explainable: counting, ranking and a small weighted score, no machine learning.
/// Every result says why it was chosen.
/// </para>
/// <para>
/// Safety rules that apply to every method: only dishes that can be ordered right now are recommended (not
/// unavailable dishes, not dishes in a hidden category), cancelled orders never count, and when a customer is
/// given, a dish that contains a dietary tag that customer avoids is never recommended.
/// </para>
/// </remarks>
public interface IRecommendationService
{
    /// <summary>
    /// The most popular dishes: portions ordered in the last PopularityWindowDays days, most first.
    /// Ties go to the dish that is in more orders, then to the earlier name.
    /// </summary>
    /// <param name="count">The most dishes to return.</param>
    /// <param name="customerId">The signed-in customer, so dishes they avoid are left out. Null for a visitor.</param>
    Task<IReadOnlyList<DishRecommendation>> GetPopularAsync(int count, string? customerId = null);

    /// <summary>
    /// Dishes often ordered together with the given dishes: the other dishes that appear in the most orders
    /// that contain at least one of them. An order counts once per dish, however many of the given dishes it holds.
    /// The given dishes are never recommended back.
    /// </summary>
    /// <param name="menuItemIds">The dishes to find companions for.</param>
    /// <param name="count">The most dishes to return.</param>
    /// <param name="customerId">The signed-in customer, so dishes they avoid are left out. Null for a visitor.</param>
    Task<IReadOnlyList<DishRecommendation>> GetOftenOrderedTogetherAsync(
        IReadOnlyCollection<int> menuItemIds, int count, string? customerId = null);

    /// <summary>
    /// Dishes suited to one customer: scored from the categories they order from most, plus a small boost for
    /// popular dishes. A customer with no usable order history gets the popular list instead.
    /// </summary>
    /// <param name="customerId">The customer's user ID.</param>
    /// <param name="count">The most dishes to return.</param>
    Task<IReadOnlyList<DishRecommendation>> GetForCustomerAsync(string customerId, int count);
}
