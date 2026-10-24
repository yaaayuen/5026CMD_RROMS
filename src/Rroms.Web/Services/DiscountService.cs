using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>Administrator tools for menu discounts. Owner: YJ.</summary>
public class DiscountService : IDiscountService
{
    private const string NotFoundMessage = "That discount no longer exists.";

    private readonly ApplicationDbContext _context;

    /// <summary>Receives the database from dependency injection.</summary>
    /// <param name="context">The database.</param>
    public DiscountService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuDiscount>> GetDiscountsAsync()
    {
        List<MenuDiscount> discounts = await _context.MenuDiscounts
            .AsNoTracking()
            .Include(d => d.Categories)
            .Include(d => d.Dishes)
            .ToListAsync();

        return discounts.OrderByDescending(d => d.CreatedAtUtc).ThenBy(d => d.Name).ToList();
    }

    /// <inheritdoc />
    public async Task<MenuDiscount?> GetDiscountAsync(int id)
    {
        return await _context.MenuDiscounts
            .Include(d => d.Categories)
            .Include(d => d.Dishes)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> SaveDiscountAsync(DiscountInput input)
    {
        List<string> errors = await ValidateAsync(input);

        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        MenuDiscount? discount;

        if (input.Id == 0)
        {
            discount = new MenuDiscount { CreatedAtUtc = DateTime.UtcNow };
            _context.MenuDiscounts.Add(discount);
        }
        else
        {
            discount = await GetDiscountAsync(input.Id);

            if (discount == null)
            {
                return ServiceResult<int>.Failure(NotFoundMessage);
            }
        }

        discount.Name = input.Name.Trim();
        discount.Percent = input.Percent;
        discount.Scope = input.Scope;
        discount.IsActive = input.IsActive;

        discount.Categories.Clear();

        if (input.Scope == DiscountScope.Category)
        {
            List<int> categoryIds = input.CategoryIds.Distinct().ToList();
            List<MenuCategory> categories = await _context.MenuCategories.Where(c => categoryIds.Contains(c.Id)).ToListAsync();

            foreach (MenuCategory category in categories)
            {
                discount.Categories.Add(category);
            }
        }

        discount.Dishes.Clear();

        if (input.Scope == DiscountScope.Dishes)
        {
            List<int> ids = input.DishIds.Distinct().ToList();
            List<MenuItem> dishes = await _context.MenuItems.Where(item => ids.Contains(item.Id)).ToListAsync();

            foreach (MenuItem dish in dishes)
            {
                discount.Dishes.Add(dish);
            }
        }

        discount.StartUtc = input.StartDate.HasValue ? MalaysiaTime.ToUtc(input.StartDate.Value.Date) : null;
        discount.EndUtc = input.EndDate.HasValue ? MalaysiaTime.ToUtc(input.EndDate.Value.Date.AddDays(1)) : null;

        int mask = 0;

        foreach (int bit in input.SelectedDays.Distinct())
        {
            mask |= bit;
        }

        discount.DaysOfWeek = mask == 127 ? 0 : mask;

        if (input.FromTime.HasValue && input.ToTime.HasValue)
        {
            discount.FromMinute = input.FromTime.Value.Hour * 60 + input.FromTime.Value.Minute;
            discount.ToMinute = input.ToTime.Value.Hour * 60 + input.ToTime.Value.Minute;
        }
        else
        {
            discount.FromMinute = null;
            discount.ToMinute = null;
        }

        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(discount.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetActiveAsync(int id, bool isActive)
    {
        MenuDiscount? discount = await _context.MenuDiscounts.FindAsync(id);

        if (discount == null)
        {
            return ServiceResult.Failure(NotFoundMessage);
        }

        discount.IsActive = isActive;
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> DeleteDiscountAsync(int id)
    {
        MenuDiscount? discount = await _context.MenuDiscounts.FindAsync(id);

        if (discount == null)
        {
            return ServiceResult.Failure(NotFoundMessage);
        }

        _context.MenuDiscounts.Remove(discount);
        await _context.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRunningBannersAsync()
    {
        DiscountSet set = await DiscountSet.LoadAsync(_context);
        DateTime now = DateTime.UtcNow;

        // Only discounts that are running at this moment get a banner.
        return set.Discounts
            .Where(discount => DiscountRules.IsRunning(discount, now))
            .OrderByDescending(discount => discount.Percent)
            .Select(DiscountText.Banner)
            .ToList();
    }

    /// <summary>Checks every rule and returns all the problems at once.</summary>
    private async Task<List<string>> ValidateAsync(DiscountInput input)
    {
        List<string> errors = new List<string>();

        string name = (input.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            errors.Add("Enter a name for the discount.");
        }
        else if (name.Length > MenuDiscount.NameMaxLength)
        {
            errors.Add($"The name can be at most {MenuDiscount.NameMaxLength} characters.");
        }

        if (input.Percent < MenuDiscount.MinPercent || input.Percent > MenuDiscount.MaxPercent)
        {
            errors.Add($"The percentage must be between {MenuDiscount.MinPercent} and {MenuDiscount.MaxPercent}.");
        }

        if (input.Scope == DiscountScope.Category)
        {
            List<int> ids = input.CategoryIds.Distinct().ToList();
            int found = ids.Count == 0 ? 0 : await _context.MenuCategories.CountAsync(c => ids.Contains(c.Id));

            if (found == 0)
            {
                errors.Add("Choose at least one category.");
            }
        }

        if (input.Scope == DiscountScope.Dishes)
        {
            List<int> ids = input.DishIds.Distinct().ToList();
            int count = ids.Count == 0 ? 0 : await _context.MenuItems.CountAsync(item => ids.Contains(item.Id));

            if (count == 0)
            {
                errors.Add("Tick at least one dish.");
            }
        }

        if (input.StartDate.HasValue && input.EndDate.HasValue && input.EndDate.Value.Date < input.StartDate.Value.Date)
        {
            errors.Add("The last day cannot be before the first day.");
        }

        if (input.FromTime.HasValue != input.ToTime.HasValue)
        {
            errors.Add("Fill in both the start and the end time, or leave both empty for all day.");
        }
        else if (input.FromTime.HasValue && input.ToTime.HasValue && input.ToTime.Value <= input.FromTime.Value)
        {
            errors.Add("The end time must be later than the start time.");
        }

        foreach (int bit in input.SelectedDays)
        {
            if (bit < 1 || bit > 127 || (bit & (bit - 1)) != 0)
            {
                errors.Add("Pick the days from the list.");
                break;
            }
        }

        return errors;
    }
}
