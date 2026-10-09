using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IMenuService"/>: the business rules for menu item administration (YJ-1).
/// </summary>
public class MenuService : IMenuService
{
    /// <summary>Smallest price allowed: one sen.</summary>
    public const decimal MinPrice = 0.01m;

    /// <summary>Error shown when the name is empty.</summary>
    public const string NameRequiredMessage = "Enter a name for the dish.";

    /// <summary>Error shown when the price is zero or negative.</summary>
    public const string PriceMessage = "The price must be greater than zero.";

    /// <summary>Error shown when the chosen category does not exist.</summary>
    public const string CategoryNotFoundMessage = "That menu category does not exist.";

    /// <summary>Error shown when the chosen category is switched off.</summary>
    public const string InactiveCategoryMessage = "That menu category is not active. Choose another one.";

    /// <summary>Error shown when another item in the category already has this name.</summary>
    public const string DuplicateNameMessage = "This category already has a dish with that name.";

    /// <summary>Error shown when only some of the offer fields are filled in.</summary>
    public const string PromoIncompleteMessage = "An offer needs a price, a start time and an end time. Fill in all three or leave all three empty.";

    /// <summary>Error shown when the offer is not cheaper than the normal price.</summary>
    public const string PromoBelowPriceMessage = "The offer price must be greater than zero and lower than the normal price.";

    /// <summary>Error shown when the offer ends before it starts.</summary>
    public const string PromoDatesMessage = "The offer must end after it starts.";

    /// <summary>Error shown when the item does not exist.</summary>
    public const string ItemNotFoundMessage = "That menu item does not exist.";

    /// <summary>Passed to validation when there is no existing item or category to compare with.</summary>
    private const int NoExistingId = 0;

    private readonly ApplicationDbContext _context;
    private readonly IRestaurantService _restaurantService;

    /// <summary>
    /// Receives the database and WL's restaurant service, which owns the menu categories.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="restaurantService">Used to look categories up, so this class never touches WL's tables directly.</param>
    public MenuService(ApplicationDbContext context, IRestaurantService restaurantService)
    {
        _context = context;
        _restaurantService = restaurantService;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuItem>> GetMenuItemsAsync(int? categoryId)
    {
        IQueryable<MenuItem> query = _context.MenuItems.Include(item => item.MenuCategory);

        if (categoryId.HasValue)
        {
            query = query.Where(item => item.MenuCategoryId == categoryId.Value);
        }

        List<MenuItem> items = await query
            .OrderBy(item => item.MenuCategory!.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();

        return items;
    }

    /// <inheritdoc />
    public async Task<MenuItem?> GetMenuItemAsync(int id)
    {
        MenuItem? item = await _context.MenuItems
            .Include(i => i.MenuCategory)
            .FirstOrDefaultAsync(i => i.Id == id);

        return item;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateMenuItemAsync(MenuItemInput input)
    {
        List<string> errors = await ValidateAsync(input, NoExistingId, NoExistingId);

        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        DateTime now = DateTime.UtcNow;

        MenuItem item = new MenuItem
        {
            CreatedAtUtc = now
        };

        ApplyInput(item, input, now);

        _context.MenuItems.Add(item);
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(item.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateMenuItemAsync(int id, MenuItemInput input)
    {
        MenuItem? item = await _context.MenuItems.FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        List<string> errors = await ValidateAsync(input, item.Id, item.MenuCategoryId);

        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        ApplyInput(item, input, DateTime.UtcNow);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetAvailabilityAsync(int id, bool isAvailable)
    {
        MenuItem? item = await _context.MenuItems.FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        item.IsAvailable = isAvailable;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> DeleteMenuItemAsync(int id)
    {
        MenuItem? item = await _context.MenuItems.FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        _context.MenuItems.Remove(item);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <summary>
    /// Checks every business rule and returns all the problems at once, so the admin can fix them in one go.
    /// </summary>
    /// <param name="input">The values to check.</param>
    /// <param name="existingItemId">The item being edited, or 0 when creating. It is left out of the duplicate-name check.</param>
    /// <param name="currentCategoryId">The category the item is in now, or 0 when creating. An edit may keep a category that was switched off since.</param>
    private async Task<List<string>> ValidateAsync(MenuItemInput input, int existingItemId, int currentCategoryId)
    {
        List<string> errors = new List<string>();
        string name = input.Name.Trim();

        if (name.Length == 0)
        {
            errors.Add(NameRequiredMessage);
        }

        if (input.Price < MinPrice)
        {
            errors.Add(PriceMessage);
        }

        MenuCategory? category = await _restaurantService.GetMenuCategoryAsync(input.MenuCategoryId);

        if (category == null)
        {
            errors.Add(CategoryNotFoundMessage);
        }
        else if (!category.IsActive && input.MenuCategoryId != currentCategoryId)
        {
            errors.Add(InactiveCategoryMessage);
        }

        if (name.Length > 0 && category != null)
        {
            // Lower-case both sides: SQL Server ignores case but the SQLite test database does not.
            string lowerName = name.ToLower();

            bool duplicate = await _context.MenuItems.AnyAsync(item =>
                item.MenuCategoryId == input.MenuCategoryId
                && item.Id != existingItemId
                && item.Name.ToLower() == lowerName);

            if (duplicate)
            {
                errors.Add(DuplicateNameMessage);
            }
        }

        AddPromoErrors(input, errors);

        return errors;
    }

    /// <summary>
    /// An offer is either completely empty or completely filled in, cheaper than the normal price, with sensible dates.
    /// </summary>
    private static void AddPromoErrors(MenuItemInput input, List<string> errors)
    {
        decimal? promoPrice = input.PromoPrice;
        DateTime? start = input.PromoStartUtc;
        DateTime? end = input.PromoEndUtc;

        bool hasAnyPromoField = promoPrice.HasValue || start.HasValue || end.HasValue;
        bool hasAllPromoFields = promoPrice.HasValue && start.HasValue && end.HasValue;

        if (hasAnyPromoField && !hasAllPromoFields)
        {
            errors.Add(PromoIncompleteMessage);
            return;
        }

        if (promoPrice.HasValue && start.HasValue && end.HasValue)
        {
            if (promoPrice.Value < MinPrice || promoPrice.Value >= input.Price)
            {
                errors.Add(PromoBelowPriceMessage);
            }

            if (end.Value <= start.Value)
            {
                errors.Add(PromoDatesMessage);
            }
        }
    }

    /// <summary>Copies the validated form values onto the entity.</summary>
    private static void ApplyInput(MenuItem item, MenuItemInput input, DateTime now)
    {
        item.Name = input.Name.Trim();
        item.Description = input.Description.Trim();
        item.MenuCategoryId = input.MenuCategoryId;
        item.Price = input.Price;
        item.IsAvailable = input.IsAvailable;
        item.PromoPrice = input.PromoPrice;
        item.PromoStartUtc = input.PromoStartUtc;
        item.PromoEndUtc = input.PromoEndUtc;
        item.UpdatedAtUtc = now;
    }
}