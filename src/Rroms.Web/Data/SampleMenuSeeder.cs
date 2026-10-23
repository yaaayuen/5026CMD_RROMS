using Microsoft.EntityFrameworkCore;

using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Data;

/// <summary>
/// Adds the team's sample dishes to the menu, so every teammate who runs the app sees the same menu (YJ-1).
/// </summary>
/// <remarks>
/// <para>
/// Menu items live in each developer's own LocalDB, not in git. This seeder fills the gap: at start-up it adds
/// every sample dish that is not on the menu yet (matched by name, ignoring case). It never changes or deletes a
/// dish, so prices and photos edited by hand are safe, and running it twice adds nothing.
/// </para>
/// <para>
/// The photos are the files in wwwroot/uploads/menu, committed to git (see .gitignore). The categories and dietary
/// tags are found by name; they come from the migrations. If one is missing the dish or tag is skipped with a
/// warning instead of stopping the application.
/// </para>
/// <para>
/// Not used by the automated tests: <see cref="DatabaseInitializer"/> skips it in the Testing environment, so the
/// integration tests still start with an empty menu.
/// </para>
/// </remarks>
public class SampleMenuSeeder
{
    /// <summary>One sample dish.</summary>
    /// <param name="Name">Dish name, unique on the menu.</param>
    /// <param name="CategoryName">Name of the category it belongs to.</param>
    /// <param name="Description">Text shown to customers.</param>
    /// <param name="Price">Normal price in RM.</param>
    /// <param name="ImageFileName">File in wwwroot/uploads/menu, or null for a dish without a photo.</param>
    /// <param name="TagNames">Names of the dietary tags the dish contains.</param>
    public record SampleDish(
        string Name,
        string CategoryName,
        string Description,
        decimal Price,
        string? ImageFileName,
        string[] TagNames);

    /// <summary>
    /// The sample menu, in the order the codes are handed out within a category
    /// (Starters S01, S02, S03, Grills G01, Pasta P01, P02, P03, Desserts D01, Beverages B01, B02).
    /// </summary>
    public static readonly IReadOnlyList<SampleDish> Dishes = new List<SampleDish>
    {
        new SampleDish(
            "Garlic Bread", "Starters",
            "Garlic bread is a crusty bread topped with a rich spread of garlic, butter or olive oil, and fresh herbs like parsley or oregano.",
            8.00m, "8049a488510845c282e3e2ca375c7cce.jpg", new string[0]),

        new SampleDish(
            "Mushroom Soup", "Starters",
            "Mushroom soup is a savory, comforting dish made by simmering fresh or wild mushrooms with aromatics like onions, garlic, and herbs in a broth or cream-based liquid.",
            13.00m, "6703a29c489543bea666e57562a89b26.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Salad", "Starters",
            "A salad is a versatile dish typically consisting of a mixture of raw or cooked ingredients, almost always accompanied by a flavorful dressing.",
            11.00m, "f94e365f115049dea3bc95a714dd0512.jpg", new string[0]),

        new SampleDish(
            "Smoky BBQ Burger", "Grills",
            "A Smoky BBQ Burger is a rich, savory classic that delivers bold, flame-grilled flavor in every bite. It centers around a juicy, perfectly seasoned beef patty—often infused with wood-fire smoke, liquid smoke, or a robust dry rub.",
            28.00m, "6dc9f1588d454147a16874091f41f329.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Bacon Carbonara", "Pasta",
            "A classic bacon carbonara is a rich and glossy Roman-inspired pasta dish made by combining hot pasta, crispy bacon, eggs, grated cheese, and starchy cooking water",
            30.00m, "4342bf1040874838acac3a921644916a.jpg", new[] { "Animal products", "Dairy" }),

        new SampleDish(
            "Creamy Gochujang Pasta", "Pasta",
            "Creamy gochujang pasta is a popular Korean-Italian fusion dish that combines the fiery, fermented umami depth of traditional Korean chili paste with a rich, velvety Western-style cream and parmesan sauce.",
            32.00m, "bcc07f44665746acbbfe4d875f6e856d.jpg", new[] { "Dairy", "Spicy" }),

        new SampleDish(
            "Pesto Pasta", "Pasta",
            "Pesto pasta is a quick Italian dish made by tossing cooked pasta with a vibrant sauce of fresh basil, garlic, pine nuts (or walnuts), Parmesan cheese, and olive oil.",
            32.00m, "e4713ca0daf34a19b6d104e54c6d4bb4.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Tiramisu", "Desserts",
            "Tiramisu is a famous no-bake Italian dessert made of coffee-soaked ladyfingers layered with a rich cream of mascarpone cheese, eggs, and sugar, finished with a dusting of cocoa powder",
            20.00m, "7a91e5c3a3a640b9bbfc5882fb2cbd5c.webp", new[] { "Dairy" }),

        new SampleDish(
            "Coca-cola", "Beverages",
            "Coca-Cola is a sweetened, carbonated soft drink",
            5.00m, null, new string[0]),

        new SampleDish(
            "Orange Juice", "Beverages",
            "Orange juice is a popular fruit beverage made by extracting the liquid from orange tree fruits (Citrus sinensis), using Valencia or Navel oranges.",
            8.00m, "4861adba4f93467ab0f1bbd9f4450ecf.jpg", new string[0])
    };

    private readonly ApplicationDbContext _context;
    private readonly ILogger<SampleMenuSeeder> _logger;

    /// <summary>Receives the database and a logger.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">Used for the "skipped" warnings.</param>
    public SampleMenuSeeder(ApplicationDbContext context, ILogger<SampleMenuSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Adds every sample dish that is not on the menu yet and returns how many were added.</summary>
    public async Task<int> SeedAsync()
    {
        List<string> existingNames = await _context.MenuItems.Select(item => item.Name).ToListAsync();
        HashSet<string> takenNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        List<string?> existingCodes = await _context.MenuItems.Select(item => item.Code).ToListAsync();
        List<string?> usedCodes = new List<string?>(existingCodes);

        List<MenuCategory> categories = await _context.MenuCategories.ToListAsync();
        List<DietaryTag> tags = await _context.DietaryTags.ToListAsync();

        int added = 0;

        foreach (SampleDish dish in Dishes)
        {
            if (takenNames.Contains(dish.Name))
            {
                continue;
            }

            MenuCategory? category = categories.FirstOrDefault(
                c => string.Equals(c.Name, dish.CategoryName, StringComparison.OrdinalIgnoreCase));

            if (category == null)
            {
                _logger.LogWarning(
                    "Sample dish {Dish} skipped: category {Category} does not exist.", dish.Name, dish.CategoryName);
                continue;
            }

            List<DietaryTag> dishTags = new List<DietaryTag>();

            foreach (string tagName in dish.TagNames)
            {
                DietaryTag? tag = tags.FirstOrDefault(
                    t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));

                if (tag == null)
                {
                    _logger.LogWarning(
                        "Dietary tag {Tag} not found, so sample dish {Dish} is added without it.", tagName, dish.Name);
                    continue;
                }

                dishTags.Add(tag);
            }

            string code = MenuCodeRule.Next(category.Name, usedCodes);
            DateTime now = DateTime.UtcNow;

            _context.MenuItems.Add(new MenuItem
            {
                Code = code,
                Name = dish.Name,
                Description = dish.Description,
                MenuCategoryId = category.Id,
                Price = dish.Price,
                IsAvailable = true,
                DietaryTags = dishTags,
                ImageFileName = dish.ImageFileName,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            usedCodes.Add(code);
            takenNames.Add(dish.Name);
            added++;
        }

        if (added > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Added {Count} sample dishes to the menu.", added);
        }

        return added;
    }
}
