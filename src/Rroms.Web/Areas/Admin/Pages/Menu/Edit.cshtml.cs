using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Menu;

/// <summary>
/// Add or edit one dish, including its photo. Owner: YJ (YJ-1).
/// </summary>
/// <remarks>
/// /Admin/Menu/Edit adds a dish, /Admin/Menu/Edit/5 edits dish 5.
/// Discounts are no longer set per dish: the administrator uses the Promotions page. Saving a dish here
/// clears any old offer it still had, and an offer sent by hand is ignored. The dish is saved first; the
/// photo is saved second, so a rejected photo never loses the other changes.
/// </remarks>
public class EditModel : PageModel
{
    private const long BytesPerMegabyte = 1024 * 1024;

    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;
    private readonly IMenuImageStorage _imageStorage;
    private readonly IProfileService _profileService;
    private readonly MenuImageOptions _imageOptions;

    /// <summary>Receives the services and photo limits from dependency injection.</summary>
    /// <param name="menuService">The menu item service.</param>
    /// <param name="restaurantService">Gives the menu categories for the drop-down.</param>
    /// <param name="imageStorage">Turns a stored photo name into a web address.</param>
    /// <param name="profileService">Gives the shared dietary tags (YY's table) for the tick boxes.</param>
    /// <param name="imageOptions">The photo limits, shown to the administrator as a hint.</param>
    public EditModel(
        IMenuService menuService,
        IRestaurantService restaurantService,
        IMenuImageStorage imageStorage,
        IProfileService profileService,
        IOptions<MenuImageOptions> imageOptions)
    {
        _menuService = menuService;
        _restaurantService = restaurantService;
        _imageStorage = imageStorage;
        _profileService = profileService;
        _imageOptions = imageOptions.Value;
    }

    /// <summary>The dish details from the form.</summary>
    [BindProperty]
    public MenuItemInput Input { get; set; } = new MenuItemInput();

    /// <summary>A new photo chosen by the administrator, or null to keep the current one.</summary>
    [BindProperty]
    [Display(Name = "Photo")]
    public IFormFile? Photo { get; set; }

    /// <summary>Ticked to remove the current photo (ignored when a new photo is uploaded).</summary>
    [BindProperty]
    [Display(Name = "Remove the current photo")]
    public bool RemovePhoto { get; set; }

    /// <summary>The dish being edited, or null when adding a new one.</summary>
    public int? EditingId { get; private set; }

    /// <summary>Name shown in the heading while editing.</summary>
    public string EditingName { get; private set; } = string.Empty;

    /// <summary>The web address of the current photo, or null when the dish has none.</summary>
    public string? CurrentPhotoUrl { get; private set; }

    /// <summary>Every dietary tag, in display order, for the tick boxes.</summary>
    public IReadOnlyList<DietaryTag> DietaryTagChoices { get; private set; } = new List<DietaryTag>();

    /// <summary>The categories offered in the drop-down.</summary>
    public IReadOnlyList<SelectListItem> CategoryChoices { get; private set; } = new List<SelectListItem>();

    /// <summary>Hint under the photo field, built from the configured limits.</summary>
    public string PhotoHelpText
    {
        get
        {
            long megabytes = _imageOptions.MaxFileSizeBytes / BytesPerMegabyte;
            return $"JPG, PNG or WebP, up to {megabytes} MB. Wider photos are scaled down automatically.";
        }
    }

    /// <summary>The smallest price, formatted for an HTML number field.</summary>
    public string MinPriceText
    {
        get { return MenuService.MinPrice.ToString(CultureInfo.InvariantCulture); }
    }

    /// <summary>Handles GET. With an id, fills the form with that dish.</summary>
    /// <param name="id">The dish to edit, or null to show an empty "add" form.</param>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!id.HasValue)
        {
            await LoadChoicesAsync(null);
            return Page();
        }

        MenuItem? item = await _menuService.GetMenuItemAsync(id.Value);

        if (item == null)
        {
            return NotFound();
        }

        EditingId = item.Id;
        EditingName = item.Name;
        CurrentPhotoUrl = GetPhotoUrl(item);

        Input = new MenuItemInput
        {
            Name = item.Name,
            Description = item.Description,
            MenuCategoryId = item.MenuCategoryId,
            Price = item.Price,
            IsAvailable = item.IsAvailable,
            CookingMinutes = item.CookingMinutes,
            DietaryTagIds = item.DietaryTags.Select(tag => tag.Id).ToList()
        };

        await LoadChoicesAsync(item.MenuCategoryId);
        return Page();
    }

    /// <summary>Handles POST. Without an id it adds a dish, with an id it updates that dish.</summary>
    /// <param name="id">The dish being edited, or null when adding.</param>
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id;
        int? originalCategoryId = null;

        if (id.HasValue)
        {
            MenuItem? existing = await _menuService.GetMenuItemAsync(id.Value);

            if (existing == null)
            {
                return NotFound();
            }

            EditingName = existing.Name;
            CurrentPhotoUrl = GetPhotoUrl(existing);
            originalCategoryId = existing.MenuCategoryId;
        }

        // Dish offers are retired (discounts are Promotions now). Always clear them: a request made by hand
        // must not be able to set one, and saving a dish removes any old offer it still had.
        Input.PromoPrice = null;
        Input.PromoStartUtc = null;
        Input.PromoEndUtc = null;

        if (!ModelState.IsValid)
        {
            await LoadChoicesAsync(originalCategoryId);
            return Page();
        }

        int itemId;

        if (id.HasValue)
        {
            ServiceResult updateResult = await _menuService.UpdateMenuItemAsync(id.Value, Input);

            if (!updateResult.Succeeded)
            {
                AddErrorsToModelState(updateResult.Errors);
                await LoadChoicesAsync(originalCategoryId);
                return Page();
            }

            itemId = id.Value;
        }
        else
        {
            ServiceResult<int> createResult = await _menuService.CreateMenuItemAsync(Input);

            if (!createResult.Succeeded)
            {
                AddErrorsToModelState(createResult.Errors);
                await LoadChoicesAsync(originalCategoryId);
                return Page();
            }

            itemId = createResult.Value;
        }

        string? photoError = await SavePhotoChangesAsync(itemId);

        if (photoError != null)
        {
            // The dish itself is saved, so open it in edit mode and explain what went wrong with the photo.
            TempData["PhotoError"] = "The dish was saved, but the photo was not: " + photoError;
            return RedirectToPage(new { id = itemId });
        }

        TempData["StatusMessage"] = id.HasValue ? "Dish updated." : "Dish added.";
        return RedirectToPage("Index");
    }

    /// <summary>
    /// Uploads a new photo, or removes the current one when asked. Returns an error message, or null when all went well.
    /// </summary>
    /// <param name="itemId">The dish that was just saved.</param>
    private async Task<string?> SavePhotoChangesAsync(int itemId)
    {
        if (Photo != null)
        {
            await using Stream stream = Photo.OpenReadStream();
            ServiceResult uploadResult = await _menuService.SetImageAsync(itemId, stream, Photo.FileName);

            if (!uploadResult.Succeeded)
            {
                return string.Join(" ", uploadResult.Errors);
            }

            return null;
        }

        if (RemovePhoto)
        {
            ServiceResult removeResult = await _menuService.RemoveImageAsync(itemId);

            if (!removeResult.Succeeded)
            {
                return string.Join(" ", removeResult.Errors);
            }
        }

        return null;
    }

    /// <summary>Fills everything the form needs to draw its choices: the categories and the dietary tags.</summary>
    /// <param name="keepCategoryId">The dish's current category, or null when adding.</param>
    private async Task LoadChoicesAsync(int? keepCategoryId)
    {
        DietaryTagChoices = await _profileService.GetDietaryTagsAsync();

        await LoadCategoryChoicesAsync(keepCategoryId);
    }

    /// <summary>
    /// Fills the category drop-down with the active categories. When editing, the dish's own category is
    /// always included, even if it was hidden since, so saving does not force a change of category.
    /// </summary>
    /// <param name="keepCategoryId">The dish's current category, or null when adding.</param>
    private async Task LoadCategoryChoicesAsync(int? keepCategoryId)
    {
        IReadOnlyList<MenuCategory> activeCategories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: false);
        List<MenuCategory> categories = new List<MenuCategory>(activeCategories);

        if (keepCategoryId.HasValue)
        {
            bool alreadyListed = categories.Any(category => category.Id == keepCategoryId.Value);

            if (!alreadyListed)
            {
                MenuCategory? current = await _restaurantService.GetMenuCategoryAsync(keepCategoryId.Value);

                if (current != null)
                {
                    categories.Add(current);
                }
            }
        }

        List<SelectListItem> choices = new List<SelectListItem>();

        foreach (MenuCategory category in categories)
        {
            string label = category.IsActive ? category.Name : category.Name + " (hidden)";
            choices.Add(new SelectListItem(label, category.Id.ToString(CultureInfo.InvariantCulture)));
        }

        CategoryChoices = choices;
    }

    /// <summary>Copies service error messages into the page so the form shows them.</summary>
    private void AddErrorsToModelState(IReadOnlyList<string> errors)
    {
        foreach (string error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    /// <summary>The web address of a dish's photo, or null when it has none.</summary>
    private string? GetPhotoUrl(MenuItem item)
    {
        if (string.IsNullOrEmpty(item.ImageFileName))
        {
            return null;
        }

        return _imageStorage.GetPublicUrl(item.ImageFileName);
    }
}
