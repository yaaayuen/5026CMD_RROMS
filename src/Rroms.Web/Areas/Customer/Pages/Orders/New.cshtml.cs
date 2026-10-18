using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Orders;

/// <summary>
/// Order food for one of the customer's reservations (YJ-3): the form IS the cart. Owner: YJ.
/// </summary>
/// <remarks>
/// /Customer/Orders/New/5 orders for reservation 5. The page lists every dish with a quantity box; the
/// browser sends only dish Ids, quantities and notes. The service re-reads every price and availability from
/// the database, so nothing the browser sends can change what is charged. A reservation that is not the
/// customer's own returns 404, like one that does not exist.
/// </remarks>
public class NewModel : PageModel
{
    private const int RecommendedCount = 4;

    private readonly IOrderService _orderService;
    private readonly IMenuService _menuService;
    private readonly IRecommendationService _recommendationService;
    private readonly OrderRulesOptions _rules;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="orderService">Places the order.</param>
    /// <param name="menuService">Lists the dishes with their current prices.</param>
    /// <param name="recommendationService">Suggests dishes the customer may like (YJ-4).</param>
    /// <param name="options">The ordering limits, used to limit the quantity boxes.</param>
    public NewModel(
        IOrderService orderService,
        IMenuService menuService,
        IRecommendationService recommendationService,
        IOptions<OrderRulesOptions> options)
    {
        _orderService = orderService;
        _menuService = menuService;
        _recommendationService = recommendationService;
        _rules = options.Value;
    }

    /// <summary>The quantities (and notes) from the form, one entry per dish shown.</summary>
    [BindProperty]
    public List<PlaceOrderLine> Lines { get; set; } = new List<PlaceOrderLine>();

    /// <summary>The note for the whole order.</summary>
    [BindProperty]
    public string? Notes { get; set; }

    /// <summary>The booking being ordered for, or null when it cannot be ordered for right now.</summary>
    public OrderableReservation? Reservation { get; private set; }

    /// <summary>Why the booking cannot be ordered for, or null when it can.</summary>
    public string? NotOrderableMessage { get; private set; }

    /// <summary>The dishes on the menu, with their current prices.</summary>
    public IReadOnlyList<MenuItemCard> Dishes { get; private set; } = new List<MenuItemCard>();

    /// <summary>The "Recommended for you" strip (YJ-4). Falls back to popular dishes when there is no history.</summary>
    public DishSuggestionSection Recommended { get; private set; } = new DishSuggestionSection();

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

    /// <summary>Handles GET /Customer/Orders/New/{reservationId}.</summary>
    /// <param name="reservationId">The reservation to order for.</param>
    public async Task<IActionResult> OnGetAsync(int reservationId)
    {
        IActionResult? stop = await LoadAsync(reservationId);

        if (stop != null)
        {
            return stop;
        }

        return Page();
    }

    /// <summary>Handles POST: places the order.</summary>
    /// <param name="reservationId">The reservation to order for.</param>
    public async Task<IActionResult> OnPostAsync(int reservationId)
    {
        // Loads the booking and the menu again: needed to show the form again if the order is refused,
        // and it re-checks that the booking is still the customer's own and still orderable.
        IActionResult? stop = await LoadAsync(reservationId);

        if (stop != null)
        {
            return stop;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // The form lists every dish, so most rows have quantity 0 and are simply left out.
        // A negative number is NOT left out: it goes to the service, which refuses it.
        PlaceOrderRequest request = new PlaceOrderRequest
        {
            ReservationId = reservationId,
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

        ServiceResult<int> result = await _orderService.PlaceOrderAsync(User.GetUserId(), request);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = "Your order has been placed.";
        return RedirectToPage("Details", new { id = result.Value });
    }

    /// <summary>The quantity to show in a dish's box: what the customer typed before, or 0.</summary>
    /// <param name="dishId">The dish.</param>
    public int QuantityFor(int dishId)
    {
        PlaceOrderLine? line = Lines.FirstOrDefault(l => l.MenuItemId == dishId);

        if (line == null)
        {
            return 0;
        }

        return line.Quantity;
    }

    /// <summary>The note to show in a dish's note box: what the customer typed before, or empty.</summary>
    /// <param name="dishId">The dish.</param>
    public string NoteFor(int dishId)
    {
        PlaceOrderLine? line = Lines.FirstOrDefault(l => l.MenuItemId == dishId);

        if (line == null || line.Notes == null)
        {
            return string.Empty;
        }

        return line.Notes;
    }

    /// <summary>
    /// Loads the booking and the menu. Returns a result to stop with (404 for a booking that is not the
    /// customer's, or the page with an explanation when it cannot be ordered for), or null to carry on.
    /// </summary>
    private async Task<IActionResult?> LoadAsync(int reservationId)
    {
        ServiceResult<OrderableReservation> result =
            await _orderService.GetOrderableReservationAsync(User.GetUserId(), reservationId);

        if (!result.Succeeded)
        {
            // "Not found" covers both a missing booking and someone else's, so nothing is revealed.
            if (result.Errors.Contains(OrderService.ReservationNotFoundMessage))
            {
                return NotFound();
            }

            NotOrderableMessage = string.Join(" ", result.Errors);
            return Page();
        }

        Reservation = result.Value;
        Dishes = await _menuService.SearchMenuAsync(new MenuSearchCriteria());

        Recommended = new DishSuggestionSection
        {
            Heading = "Recommended for you",
            Key = "recommended",
            Items = await _recommendationService.GetForCustomerAsync(User.GetUserId(), RecommendedCount)
        };

        return null;
    }
}
