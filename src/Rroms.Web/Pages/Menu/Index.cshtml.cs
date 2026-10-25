using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Menu;

/// <summary>
/// The customer menu: every dish with its photo, description, current price, running offer and
/// availability, with search and category/price filters. Public: anyone can open it. Owner: YJ (YJ-2).
/// </summary>
/// <remarks>
/// /Menu shows everything, /Menu?q=burger&amp;categoryId=2&amp;maxPrice=30 filters it. The filter form uses GET,
/// so a filtered menu has its own address that can be bookmarked or shared. All pricing and availability
/// decisions are made in <see cref="IMenuService"/>; this page only displays what the service returns.
/// A "Popular this month" strip (YJ-4) appears above the full menu when no filter is in use; for a signed-in
/// customer it leaves out dishes containing a tag they avoid.
/// <para>
/// Ordering from the menu (YJ-3): /Menu is ALWAYS the plain menu first. A signed-in customer with a reservation that
/// can take an order sees a "Start ordering" button; /Menu?order=1 turns on the ordering view (steppers, an
/// "Ordering for" selector, the order bar). Placing an order from here only ADDS an order to the reservation (the
/// bill already adds a visit's orders together). The server checks the reservation belongs to the customer, like
/// the order page does.
/// </para>
/// </remarks>
public class IndexModel : PageModel
{
    private const string OfferEndFormat = "d MMM yyyy, h:mm tt";
    private const int PopularCount = 4;

    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;
    private readonly IProfileService _profileService;
    private readonly IRecommendationService _recommendationService;
    private readonly IDiscountService _discountService;
    private readonly IOrderService _orderService;
    private readonly OrderRulesOptions _rules;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="menuService">Searches the menu.</param>
    /// <param name="restaurantService">Gives the active categories for the filter drop-down.</param>
    /// <param name="profileService">Gives the shared dietary tags (YY's table) for the "hide dishes that contain" boxes.</param>
    /// <param name="recommendationService">Gives the popular dishes strip.</param>
    /// <param name="discountService">Gives the promotion banner (a sentence for each discount running now).</param>
    /// <param name="orderService">Lists the customer's reservations that can order, what they ordered, and places orders.</param>
    /// <param name="options">The ordering limits, used to limit the quantity boxes.</param>
    public IndexModel(
        IMenuService menuService,
        IRestaurantService restaurantService,
        IProfileService profileService,
        IRecommendationService recommendationService,
        IDiscountService discountService,
        IOrderService orderService,
        IOptions<OrderRulesOptions> options)
    {
        _menuService = menuService;
        _restaurantService = restaurantService;
        _profileService = profileService;
        _recommendationService = recommendationService;
        _discountService = discountService;
        _orderService = orderService;
        _rules = options.Value;
    }

    /// <summary>Words to look for in dish names and descriptions (address part ?q=).</summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchText { get; set; }

    /// <summary>Only this category (address part ?categoryId=), or null for all.</summary>
    [BindProperty(SupportsGet = true, Name = "categoryId")]
    public int? CategoryId { get; set; }

    /// <summary>Hide dishes that contain these dietary tags (address part ?hide=2&amp;hide=3, by tag Id).</summary>
    [BindProperty(SupportsGet = true, Name = "hide")]
    public List<int> HideTagIds { get; set; } = new List<int>();

    /// <summary>Lowest price to show (address part ?minPrice=).</summary>
    [BindProperty(SupportsGet = true, Name = "minPrice")]
    public decimal? MinPrice { get; set; }

    /// <summary>Highest price to show (address part ?maxPrice=).</summary>
    [BindProperty(SupportsGet = true, Name = "maxPrice")]
    public decimal? MaxPrice { get; set; }

    /// <summary>Address part ?order=1: the customer asked for the ordering view.</summary>
    [BindProperty(SupportsGet = true, Name = "order")]
    public int? OrderFlag { get; set; }

    /// <summary>Address part ?reservationId= (and the form field of the same name): the reservation being ordered for.</summary>
    [BindProperty(SupportsGet = true, Name = "reservationId")]
    public int? ReservationId { get; set; }

    /// <summary>The quantities (and notes) from the order form, one entry per orderable dish shown.</summary>
    [BindProperty]
    public List<PlaceOrderLine> Lines { get; set; } = new List<PlaceOrderLine>();

    /// <summary>The note for the whole order.</summary>
    [BindProperty]
    public string? Notes { get; set; }

    /// <summary>True when somebody is signed in (the page then offers ordering instead of "Sign in to order").</summary>
    public bool IsSignedIn { get; private set; }

    /// <summary>True when the signed-in user is a customer (staff and administrators do not order here).</summary>
    public bool IsCustomer { get; private set; }

    /// <summary>The customer's own reservations that can take an order (Confirmed or Seated), same rule as the order page.</summary>
    public IReadOnlyList<OrderableReservation> OrderableReservations { get; private set; } = new List<OrderableReservation>();

    /// <summary>The reservation being ordered for, or null when the customer has none that can order.</summary>
    public OrderableReservation? Selected { get; private set; }

    /// <summary>True when the ordering view is on: a customer with a reservation who asked for it.</summary>
    public bool OrderingOn { get; private set; }

    /// <summary>The names of the dietary tags the customer avoids (ordering view only), for the warnings on the cards.</summary>
    public IReadOnlySet<string> AvoidedTagNames { get; private set; } = new HashSet<string>();

    /// <summary>The largest quantity of one dish that can be ordered.</summary>
    public int MaxQuantity
    {
        get { return _rules.MaxQuantityPerLine; }
    }

    /// <summary>Longest note for one dish.</summary>
    public int MaxLineNoteLength
    {
        get { return OrderItem.NotesMaxLength; }
    }

    /// <summary>Longest note for the whole order.</summary>
    public int MaxOrderNoteLength
    {
        get { return Order.NotesMaxLength; }
    }

    /// <summary>The dishes to show, in category then name order.</summary>
    public IReadOnlyList<MenuItemCard> Dishes { get; private set; } = new List<MenuItemCard>();

    /// <summary>One sentence for each promotion running right now (the banner above the dishes).</summary>
    public IReadOnlyList<string> Promotions { get; private set; } = new List<string>();

    /// <summary>The categories customers can pick in the filter.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>Every dietary tag, in display order, for the "hide dishes that contain" boxes.</summary>
    public IReadOnlyList<DietaryTag> TagChoices { get; private set; } = new List<DietaryTag>();

    /// <summary>The "Popular this month" strip. Empty when a filter is in use or nothing was ordered lately.</summary>
    public DishSuggestionSection Popular { get; private set; } = new DishSuggestionSection();

    /// <summary>True when any filter is in use, so the page can offer a "show everything" link.</summary>
    public bool HasFilters
    {
        get
        {
            bool hasText = !string.IsNullOrWhiteSpace(SearchText);

            return hasText || CategoryId.HasValue || HideTagIds.Count > 0 || MinPrice.HasValue || MaxPrice.HasValue;
        }
    }

    /// <summary>Handles GET /Menu.</summary>
    public async Task OnGetAsync()
    {
        await LoadCustomerAsync();
        OrderingOn = OrderFlag == 1 && Selected != null;

        await LoadMenuAsync();
    }

    /// <summary>Handles POST /Menu?handler=PlaceOrder: adds an order to the selected reservation.</summary>
    public async Task<IActionResult> OnPostPlaceOrderAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        if (!User.IsInRole(RoleNames.Customer))
        {
            return Forbid();
        }

        // The form lists every dish, so most rows have quantity 0 and are simply left out.
        // A negative number is NOT left out: it goes to the service, which refuses it.
        PlaceOrderRequest request = new PlaceOrderRequest
        {
            ReservationId = ReservationId ?? 0,
            Notes = Notes
        };

        foreach (PlaceOrderLine line in Lines)
        {
            if (line.Quantity != 0)
            {
                request.Lines.Add(new PlaceOrderLine
                {
                    MenuItemId = line.MenuItemId,
                    Quantity = line.Quantity,
                    Notes = line.Notes
                });
            }
        }

        // The service checks the reservation is the customer's own and still orderable, exactly like the order page.
        ServiceResult<int> result = await _orderService.PlaceOrderAsync(User.GetUserId(), request);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Your order has been placed.";

            return RedirectToPage("/Orders/Details", new { area = "Customer", id = result.Value });
        }

        foreach (string error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        // Show the ordering view again with what the customer typed.
        await LoadCustomerAsync();
        OrderingOn = Selected != null;
        await LoadMenuAsync();

        return Page();
    }

    /// <summary>The tags of a dish that the customer avoids (empty when none). A guide only: it never blocks an order.</summary>
    /// <param name="dish">The dish.</param>
    public IReadOnlyList<string> AvoidedIn(MenuItemCard dish)
    {
        return dish.DietaryTags.Where(tag => AvoidedTagNames.Contains(tag)).ToList();
    }

    /// <summary>The quantity to show in a dish's box: what the customer typed before, or 0.</summary>
    /// <param name="dishId">The dish.</param>
    public int QuantityFor(int dishId)
    {
        PlaceOrderLine? line = Lines.FirstOrDefault(l => l.MenuItemId == dishId);

        return line == null ? 0 : line.Quantity;
    }

    /// <summary>The note to show in a dish's note box: what the customer typed before, or empty.</summary>
    /// <param name="dishId">The dish.</param>
    public string NoteFor(int dishId)
    {
        PlaceOrderLine? line = Lines.FirstOrDefault(l => l.MenuItemId == dishId);

        return line?.Notes ?? string.Empty;
    }

    /// <summary>How many of the dish the reservation has already ordered (cancelled orders do not count).</summary>
    /// <param name="reservationId">The reservation.</param>
    /// <param name="dishId">The dish.</param>
    public int AlreadyOrdered(int reservationId, int dishId)
    {
        if (_ordered.TryGetValue(reservationId, out IReadOnlyDictionary<int, int>? dishes)
            && dishes.TryGetValue(dishId, out int quantity))
        {
            return quantity;
        }

        return 0;
    }

    /// <summary>
    /// "reservationId:count|reservationId:count" for one dish, for every reservation that ordered it. Empty when none did.
    /// The page script uses it to change the "Already ordered" badge when the customer switches reservation.
    /// </summary>
    /// <param name="dishId">The dish.</param>
    public string AlreadyOrderedText(int dishId)
    {
        List<string> parts = new List<string>();

        foreach (OrderableReservation reservation in OrderableReservations)
        {
            int quantity = AlreadyOrdered(reservation.Id, dishId);

            if (quantity > 0)
            {
                parts.Add($"{reservation.Id}:{quantity}");
            }
        }

        return string.Join("|", parts);
    }

    /// <summary>"Table T03 · Fri 30 Oct, 7:00 PM" for the selector and the "Adding to" line.</summary>
    /// <param name="reservation">The reservation.</param>
    public string Describe(OrderableReservation reservation)
    {
        DateTime start = MalaysiaTime.FromUtc(reservation.StartUtc);

        return $"Table {reservation.TableLabel} \u00B7 {start.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture)}";
    }

    private IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> _ordered =
        new Dictionary<int, IReadOnlyDictionary<int, int>>();

    /// <summary>Finds out who is looking and which reservation they would order for. Nothing here changes the page for a visitor.</summary>
    private async Task LoadCustomerAsync()
    {
        IsSignedIn = User.Identity?.IsAuthenticated == true;
        IsCustomer = IsSignedIn && User.IsInRole(RoleNames.Customer);
        Selected = null;

        if (!IsCustomer)
        {
            return;
        }

        string customerId = User.GetUserId();
        OrderableReservations = await _orderService.GetOrderableReservationsAsync(customerId);

        if (OrderableReservations.Count == 0)
        {
            return;
        }

        // Only the customer's own list is searched, so another customer's reservation id is simply not found
        // and the first reservation is used instead.
        Selected = OrderableReservations.FirstOrDefault(r => r.Id == ReservationId) ?? OrderableReservations[0];
        ReservationId = Selected.Id;
    }

    private async Task LoadMenuAsync()
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: false);
        Promotions = await _discountService.GetRunningBannersAsync();
        TagChoices = await _profileService.GetDietaryTagsAsync();

        var criteria = new MenuSearchCriteria
        {
            SearchText = SearchText ?? string.Empty,
            MenuCategoryId = CategoryId,
            ExcludeTagIds = HideTagIds,
            MinPrice = MinPrice,
            MaxPrice = MaxPrice
        };

        Dishes = await _menuService.SearchMenuAsync(criteria);

        if (OrderingOn)
        {
            _ordered = await _orderService.GetOrderedQuantitiesAsync(User.GetUserId());
            AvoidedTagNames = await LoadAvoidedTagNamesAsync();

            // The ordering view is about ordering: the "Popular" strip would only get in the way.
            return;
        }

        // The strip is for browsing the whole menu; with a filter on, it would only get in the way.
        if (!HasFilters)
        {
            string? customerId = null;

            if (IsSignedIn)
            {
                customerId = User.GetUserId();
            }

            Popular = new DishSuggestionSection
            {
                Heading = "Popular this month",
                Key = "popular",
                Items = await _recommendationService.GetPopularAsync(PopularCount, customerId)
            };
        }
    }

    /// <summary>The names of the tags the customer avoids. GetAvoidedTagIdsAsync already returns nothing without consent.</summary>
    private async Task<IReadOnlySet<string>> LoadAvoidedTagNamesAsync()
    {
        IReadOnlyList<int> avoidedIds = await _profileService.GetAvoidedTagIdsAsync(User.GetUserId());

        if (avoidedIds.Count == 0)
        {
            return new HashSet<string>();
        }

        return TagChoices
            .Where(tag => avoidedIds.Contains(tag.Id))
            .Select(tag => tag.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Shows when an offer ends, in Malaysia time (the database keeps UTC).</summary>
    /// <param name="offerEndsAtUtc">The offer's end time in UTC.</param>
    public string FormatOfferEnd(DateTime offerEndsAtUtc)
    {
        DateTime local = MalaysiaTime.FromUtc(offerEndsAtUtc);

        return local.ToString(OfferEndFormat, CultureInfo.InvariantCulture);
    }
}
