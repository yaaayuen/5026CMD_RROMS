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

    /// <summary>Error shown when another dish anywhere on the menu already has this name.</summary>
    public const string DuplicateNameMessage = "A dish with that name is already on the menu. Dish names must be unique.";

    /// <summary>Error shown when only some of the offer fields are filled in.</summary>
    public const string PromoIncompleteMessage = "An offer needs a price, a start time and an end time. Fill in all three or leave all three empty.";

    /// <summary>Error shown when the offer is not cheaper than the normal price.</summary>
    public const string PromoBelowPriceMessage = "The offer price must be greater than zero and lower than the normal price.";

    /// <summary>Error shown when the offer ends before it starts.</summary>
    public const string PromoDatesMessage = "The offer must end after it starts.";

    /// <summary>Error shown when an offer starts or ends in the past.</summary>
    public const string PromoInPastMessage = "An offer cannot start or end in the past. Choose a time from now on.";

    /// <summary>Error shown when a chosen dietary tag does not exist.</summary>
    public const string DietaryTagNotFoundMessage = "One of the chosen dietary tags does not exist.";

    /// <summary>Error shown when the item does not exist.</summary>
    public const string ItemNotFoundMessage = "That menu item does not exist.";

    /// <summary>Error shown when someone tries to delete a dish that customers have already ordered.</summary>
    public const string ItemHasOrdersMessage = "This dish has been ordered, so it cannot be deleted. Mark it unavailable instead.";

    /// <summary>Passed to validation when there is no existing item or category to compare with.</summary>
    private const int NoExistingId = 0;

    /// <summary>
    /// How far in the past an offer start may be. A form picks a time to the minute, so "start now" is
    /// already a few seconds old when it arrives; this keeps it valid without allowing real back-dating.
    /// </summary>
    private const int OfferStartGraceMinutes = 5;

    private readonly ApplicationDbContext _context;
    private readonly IRestaurantService _restaurantService;
    private readonly IMenuImageStorage _imageStorage;

    /// <summary>
    /// Receives the database, WL's restaurant service (which owns the menu categories) and the photo storage.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="restaurantService">Used to look categories up, so this class never touches WL's tables directly.</param>
    /// <param name="imageStorage">Checks, resizes and stores the dish photos.</param>
    public MenuService(ApplicationDbContext context, IRestaurantService restaurantService, IMenuImageStorage imageStorage)
    {
        _context = context;
        _restaurantService = restaurantService;
        _imageStorage = imageStorage;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuItem>> GetMenuItemsAsync(int? categoryId)
    {
        IQueryable<MenuItem> query = _context.MenuItems
            .Include(item => item.MenuCategory)
            .Include(item => item.DietaryTags);

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
            .Include(i => i.DietaryTags)
            .FirstOrDefaultAsync(i => i.Id == id);

        return item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuItemCard>> SearchMenuAsync(MenuSearchCriteria criteria)
    {
        // Dishes in a hidden category are hidden from customers too.
        IQueryable<MenuItem> query = _context.MenuItems
            .Include(item => item.MenuCategory)
            .Include(item => item.DietaryTags)
            .Where(item => item.MenuCategory!.IsActive);

        if (criteria.MenuCategoryId.HasValue)
        {
            query = query.Where(item => item.MenuCategoryId == criteria.MenuCategoryId.Value);
        }

        if (criteria.ExcludeTagIds.Count > 0)
        {
            // Hide a dish when it contains ANY of the tags the customer wants to avoid.
            List<int> excludedTagIds = criteria.ExcludeTagIds.Distinct().ToList();

            query = query.Where(item => !item.DietaryTags.Any(tag => excludedTagIds.Contains(tag.Id)));
        }

        // Lower-case both sides: SQL Server ignores case but the SQLite test database does not.
        string searchText = criteria.SearchText.Trim().ToLower();

        if (searchText.Length > 0)
        {
            query = query.Where(item => item.Name.ToLower().Contains(searchText)
                || item.Description.ToLower().Contains(searchText));
        }

        List<MenuItem> loaded = await query.ToListAsync();

        // Sort here rather than in SQL, so the order is the same on SQL Server and SQLite.
        List<MenuItem> items = loaded
            .OrderBy(item => item.MenuCategory!.DisplayOrder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DateTime now = DateTime.UtcNow;
        List<MenuItemCard> cards = new List<MenuItemCard>();

        foreach (MenuItem item in items)
        {
            // The price filter uses the price the customer would really pay today, so a dish on offer
            // matches its offer price. Done in C# after loading because the menu is small.
            decimal currentPrice = MenuItemPricing.GetCurrentPrice(item, now);

            if (criteria.MinPrice.HasValue && currentPrice < criteria.MinPrice.Value)
            {
                continue;
            }

            if (criteria.MaxPrice.HasValue && currentPrice > criteria.MaxPrice.Value)
            {
                continue;
            }

            cards.Add(BuildCard(item, currentPrice, now));
        }

        return cards;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateMenuItemAsync(MenuItemInput input)
    {
        List<string> errors = await ValidateAsync(input, null);

        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        DateTime now = DateTime.UtcNow;

        MenuItem item = new MenuItem
        {
            CreatedAtUtc = now
        };

        await ApplyInputAsync(item, input, now);

        _context.MenuItems.Add(item);
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Success(item.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateMenuItemAsync(int id, MenuItemInput input)
    {
        MenuItem? item = await _context.MenuItems
            .Include(i => i.DietaryTags)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        List<string> errors = await ValidateAsync(input, item);

        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        await ApplyInputAsync(item, input, DateTime.UtcNow);
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

        // Order history must keep its dishes (the database refuses too, but this gives a friendly message).
        bool hasBeenOrdered = await _context.OrderItems.AnyAsync(line => line.MenuItemId == id);

        if (hasBeenOrdered)
        {
            return ServiceResult.Failure(ItemHasOrdersMessage);
        }

        string? photoFileName = item.ImageFileName;

        _context.MenuItems.Remove(item);
        await _context.SaveChangesAsync();

        // The file is removed only after the database change succeeded, so a failed save never loses a photo.
        if (!string.IsNullOrEmpty(photoFileName))
        {
            _imageStorage.Delete(photoFileName);
        }

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetImageAsync(int id, Stream content, string originalFileName)
    {
        MenuItem? item = await _context.MenuItems.FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        ServiceResult<string> saved = await _imageStorage.SaveAsync(content, originalFileName);

        if (!saved.Succeeded)
        {
            return ServiceResult.Failure(saved.Errors.ToArray());
        }

        string? oldFileName = item.ImageFileName;

        item.ImageFileName = saved.Value;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Delete the previous photo last, after the new one is safely linked.
        if (!string.IsNullOrEmpty(oldFileName))
        {
            _imageStorage.Delete(oldFileName);
        }

        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> RemoveImageAsync(int id)
    {
        MenuItem? item = await _context.MenuItems.FirstOrDefaultAsync(i => i.Id == id);

        if (item == null)
        {
            return ServiceResult.Failure(ItemNotFoundMessage);
        }

        string? oldFileName = item.ImageFileName;

        if (string.IsNullOrEmpty(oldFileName))
        {
            return ServiceResult.Success();
        }

        item.ImageFileName = null;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _imageStorage.Delete(oldFileName);

        return ServiceResult.Success();
    }

    /// <summary>Turns a dish into the ready-to-display card the customer menu shows.</summary>
    private MenuItemCard BuildCard(MenuItem item, decimal currentPrice, DateTime now)
    {
        bool offerActive = MenuItemPricing.IsPromoActive(item, now);

        string? photoUrl = null;

        if (!string.IsNullOrEmpty(item.ImageFileName))
        {
            photoUrl = _imageStorage.GetPublicUrl(item.ImageFileName);
        }

        DateTime? offerEndsAtUtc = null;

        if (offerActive)
        {
            offerEndsAtUtc = item.PromoEndUtc;
        }

        MenuItemCard card = new MenuItemCard
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            CategoryName = item.MenuCategory?.Name ?? string.Empty,
            PhotoUrl = photoUrl,
            NormalPrice = item.Price,
            CurrentPrice = currentPrice,
            IsOfferActive = offerActive,
            OfferEndsAtUtc = offerEndsAtUtc,
            IsAvailable = item.IsAvailable,
            DietaryTags = item.DietaryTags
                .OrderBy(tag => tag.DisplayOrder)
                .Select(tag => tag.Name)
                .ToList(),
            CanOrder = item.IsAvailable
        };

        return card;
    }

    /// <summary>
    /// Checks every business rule and returns all the problems at once, so the admin can fix them in one go.
    /// </summary>
    /// <param name="input">The values to check.</param>
    /// <param name="existing">The item being edited, or null when creating. It is left out of the duplicate-name check,
    /// may keep a category that was switched off since, and may keep an offer time it already has.</param>
    private async Task<List<string>> ValidateAsync(MenuItemInput input, MenuItem? existing)
    {
        int existingItemId = NoExistingId;
        int currentCategoryId = NoExistingId;

        if (existing != null)
        {
            existingItemId = existing.Id;
            currentCategoryId = existing.MenuCategoryId;
        }

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

        if (name.Length > 0)
        {
            // A dish name is unique on the WHOLE menu, not just inside one category.
            // Lower-case both sides: SQL Server ignores case but the SQLite test database does not.
            string lowerName = name.ToLower();

            bool duplicate = await _context.MenuItems.AnyAsync(item =>
                item.Id != existingItemId
                && item.Name.ToLower() == lowerName);

            if (duplicate)
            {
                errors.Add(DuplicateNameMessage);
            }
        }

        List<int> tagIds = input.DietaryTagIds.Distinct().ToList();

        if (tagIds.Count > 0)
        {
            int foundTags = await _context.DietaryTags.CountAsync(tag => tagIds.Contains(tag.Id));

            if (foundTags != tagIds.Count)
            {
                errors.Add(DietaryTagNotFoundMessage);
            }
        }

        AddPromoErrors(input, existing, errors);

        return errors;
    }

    /// <summary>
    /// An offer is either completely empty or completely filled in, cheaper than the normal price, with sensible dates.
    /// </summary>
    private static void AddPromoErrors(MenuItemInput input, MenuItem? existing, List<string> errors)
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

            AddPastOfferErrors(start.Value, end.Value, existing, errors);
        }
    }

    /// <summary>
    /// An offer cannot be set to start or end in the past. Only times the administrator actually changed are
    /// checked, so a dish whose offer is already running (its start is in the past) can still be edited.
    /// </summary>
    private static void AddPastOfferErrors(DateTime start, DateTime end, MenuItem? existing, List<string> errors)
    {
        DateTime now = DateTime.UtcNow;

        bool startChanged = existing == null || !IsSameMinute(existing.PromoStartUtc, start);
        bool endChanged = existing == null || !IsSameMinute(existing.PromoEndUtc, end);

        bool startInPast = startChanged && start < now.AddMinutes(-OfferStartGraceMinutes);
        bool endInPast = endChanged && end <= now;

        if (startInPast || endInPast)
        {
            errors.Add(PromoInPastMessage);
        }
    }

    /// <summary>True when the stored time and the submitted time are the same minute (forms only send whole minutes).</summary>
    private static bool IsSameMinute(DateTime? stored, DateTime submitted)
    {
        if (!stored.HasValue)
        {
            return false;
        }

        long storedMinute = stored.Value.Ticks / TimeSpan.TicksPerMinute;
        long submittedMinute = submitted.Ticks / TimeSpan.TicksPerMinute;

        return storedMinute == submittedMinute;
    }

    /// <summary>Copies the validated form values onto the entity, including its dietary tags.</summary>
    private async Task ApplyInputAsync(MenuItem item, MenuItemInput input, DateTime now)
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

        await ApplyDietaryTagsAsync(item, input.DietaryTagIds);
    }

    /// <summary>Makes the dish's tags exactly the chosen ones: removes the unticked, adds the newly ticked.</summary>
    private async Task ApplyDietaryTagsAsync(MenuItem item, List<int> chosenIds)
    {
        List<int> tagIds = chosenIds.Distinct().ToList();

        List<DietaryTag> chosenTags = await _context.DietaryTags
            .Where(tag => tagIds.Contains(tag.Id))
            .ToListAsync();

        List<DietaryTag> toRemove = item.DietaryTags.Where(tag => !tagIds.Contains(tag.Id)).ToList();

        foreach (DietaryTag tag in toRemove)
        {
            item.DietaryTags.Remove(tag);
        }

        foreach (DietaryTag tag in chosenTags)
        {
            bool alreadyThere = item.DietaryTags.Any(existing => existing.Id == tag.Id);

            if (!alreadyThere)
            {
                item.DietaryTags.Add(tag);
            }
        }
    }
}
