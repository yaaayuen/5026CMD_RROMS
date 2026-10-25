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
    /// (Starters S01 to S06, Grills G01 to G06, Pasta P01 to P06, Desserts D01 to D03, Beverages B01 to B06).
    /// Keep each category's dishes in code order, so a teammate's G02 is the same dish as everyone else's.
    /// </summary>
    public static readonly IReadOnlyList<SampleDish> Dishes = new List<SampleDish>
    {
        new SampleDish(
            "Mushroom Soup", "Starters",
            "Mushroom soup is a savory, comforting dish made by simmering fresh or wild mushrooms with aromatics like onions, garlic, and herbs in a broth or cream-based liquid.",
            13.00m, "6703a29c489543bea666e57562a89b26.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Salad", "Starters",
            "A salad is a versatile dish typically consisting of a mixture of raw or cooked ingredients, almost always accompanied by a flavorful dressing.",
            11.00m, "f94e365f115049dea3bc95a714dd0512.jpg", new string[0]),

        new SampleDish(
            "Garlic Bread", "Starters",
            "Garlic bread is a crusty bread topped with a rich spread of garlic, butter or olive oil, and fresh herbs like parsley or oregano.",
            8.00m, "8049a488510845c282e3e2ca375c7cce.jpg", new string[0]),

        new SampleDish(
            "Grilled Sourdough Sammie", "Starters",
            "A Grilled Sourdough Veggie Sandwich with tangy jalapeño lime aioli and fresh summer produce stacked inside.",
            15.00m, "dce7f69829b74f588728dad6abfc79dc.jpg", new string[0]),

        new SampleDish(
            "Potato wedges", "Starters",
            "Potato wedges are irregular, wedge-shaped potato slices—frequently left unpeeled—that are baked, air-fried, or deep-fried until the exterior is crisp and the interior remains soft and fluffy",
            9.00m, "06fe6098b8804b51bc7d8bad8acb0bc7.jpg", new string[0]),

        new SampleDish(
            "Tomato Soup", "Starters",
            "Tomato soup is a classic dish made primarily from tomatoes with a smooth texture.",
            13.00m, "8fd3f98052e84539a5c1cefa0328002b.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Smoky BBQ Burger", "Grills",
            "A Smoky BBQ Burger is a rich, savory classic that delivers bold, flame-grilled flavor in every bite. It centers around a juicy, perfectly seasoned beef patty—often infused with wood-fire smoke, liquid smoke, or a robust dry rub.",
            28.00m, "6dc9f1588d454147a16874091f41f329.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Rib-eye Steak", "Grills",
            "A grilled ribeye steak is a rich, juicy, and tender cut of beef cooked over high heat to build a smoky, charred crust while keeping the interior pink and flavorful.",
            39.00m, "3d465c0522124080a0ac43fab140a841.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Grill Marinated Lamb Chop", "Grills",
            "These chops are naturally loaded with flavor and the simple herbed marinade combined with the smoky heat from the grill produces one of the most flavorful cuts of grilled meat you’ll ever serve or eat.",
            41.00m, "b7870c705c544f7786b8c32c73165875.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Cilantro Lime Chicken", "Grills",
            "Cilantro lime chicken is a bright, zesty, and savory poultry dish featuring a marinade or pan sauce made from fresh lime juice, lime zest, chopped cilantro, garlic, olive oil, and warm spices like cumin or chili flakes.",
            33.00m, "8d5b8d5f6cb14179866bd8c8e8f142c8.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Grilled Smoked Salmon", "Grills",
            "A smoke-grilled salmon fillet is a tender, flaky main course that combines rich, buttery fish with a savory, aromatic wood-smoke crust.",
            38.00m, "2fc3d7295d9b4c6fa7cf403ddd1c512b.webp", new[] { "Animal products" }),

        new SampleDish(
            "Grilled Black Pepper Chicken", "Grills",
            "A grilled black pepper chicken chop is a popular Southeast Asian-Western fusion dish featuring a juicy, boneless chicken thigh that is marinated, charred on a grill or pan, and smothered in a bold, savory, and spicy black pepper gravy.",
            38.00m, "57036ea179c74ca594bdb7073718e05f.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Pesto Pasta", "Pasta",
            "Pesto pasta is a quick Italian dish made by tossing cooked pasta with a vibrant sauce of fresh basil, garlic, pine nuts (or walnuts), Parmesan cheese, and olive oil.",
            32.00m, "e4713ca0daf34a19b6d104e54c6d4bb4.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Creamy Gochujang Pasta", "Pasta",
            "Creamy gochujang pasta is a popular Korean-Italian fusion dish that combines the fiery, fermented umami depth of traditional Korean chili paste with a rich, velvety Western-style cream and parmesan sauce.",
            32.00m, "bcc07f44665746acbbfe4d875f6e856d.jpg", new[] { "Dairy", "Spicy" }),

        new SampleDish(
            "Bacon Carbonara", "Pasta",
            "A classic bacon carbonara is a rich and glossy Roman-inspired pasta dish made by combining hot pasta, crispy bacon, eggs, grated cheese, and starchy cooking water",
            30.00m, "4342bf1040874838acac3a921644916a.jpg", new[] { "Animal products", "Dairy" }),

        new SampleDish(
            "Chicken Bolognese Pasta", "Pasta",
            "Chicken bolognese is a lighter, quicker spin on the classic Italian meat sauce. It trades traditional beef and pork for lean ground chicken, while keeping a rich, savory flavor.",
            30.00m, "0dc84f1760ac4fb8a55127f2bb982e7b.jpg", new[] { "Animal products" }),

        new SampleDish(
            "Seafood Aglio Olio", "Pasta",
            "Seafood Aglio Olio is a classic Italian-inspired garlic and olive oil pasta tossed with fresh shrimp, squid, or mixed seafood.",
            41.00m, "2907d9689cb34b0fb84bef0b512fe4a8.jpg", new[] { "Animal products", "Spicy" }),

        new SampleDish(
            "Lasagna", "Pasta",
            "Lasagna is a classic Italian baked dish made of stacked, flat or wavy pasta sheets layered with rich meat sauces, creamy cheeses, and savory fillings",
            34.00m, "148a56826f6247cbbf0e1ef0d527d76b.jpg", new[] { "Animal products", "Dairy" }),

        new SampleDish(
            "Tiramisu", "Desserts",
            "Tiramisu is a famous no-bake Italian dessert made of coffee-soaked ladyfingers layered with a rich cream of mascarpone cheese, eggs, and sugar, finished with a dusting of cocoa powder",
            20.00m, "7a91e5c3a3a640b9bbfc5882fb2cbd5c.webp", new[] { "Dairy" }),

        new SampleDish(
            "Basque Burnt Cheesecake", "Desserts",
            "A Basque burnt cheesecake is a crustless, high-heat baked cheesecake originating from the Basque region of Spain. It features a dark, caramelized, almost burnt top and a soft, creamy, custard-like center.",
            22.00m, "c5d009215b054ae0aad4708e1c1cb34d.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Lemon Blueberry Cheesecake", "Desserts",
            "Lemon blueberry cheesecake is a rich, creamy dessert that balances the bright, zesty tang of fresh lemon with the sweet, juicy burst of blueberries.",
            25.00m, "f493a4a2cd9a4bc78d79cf0445f9c43e.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Orange Juice", "Beverages",
            "Orange juice is a popular fruit beverage made by extracting the liquid from orange tree fruits (Citrus sinensis), using Valencia or Navel oranges.",
            8.00m, "4861adba4f93467ab0f1bbd9f4450ecf.jpg", new string[0]),

        new SampleDish(
            "Coca-cola", "Beverages",
            "Coca-Cola is a sweetened, carbonated soft drink",
            5.00m, null, new string[0]),

        new SampleDish(
            "Iced Matcha Latte", "Beverages",
            "An iced matcha latte is a refreshing, vibrant green tea drink made by whisking finely ground green tea powder with water, then pouring it over ice and cold milk.",
            12.00m, "99cf75d8b6e04bb18c0a15c4744d4db5.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Latte", "Beverages",
            "A caffè latte (commonly called a latte) is a smooth, mild espresso-based coffee drink made with espresso and a large portion of steamed milk.",
            12.00m, "dc549d17d3b447b4b9d6f11bec600fdb.jpg", new[] { "Dairy" }),

        new SampleDish(
            "Mix Berries Smoothie", "Beverages",
            "A mixed berry smoothie is a cold, creamy, and vibrant purple or pink beverage made by blending a combination of berries with a liquid base and a thickening agent.",
            15.00m, "a431d83f857d4b24b556535c7f0bab85.jpg", new string[0]),

        new SampleDish(
            "Banana Milkshake", "Beverages",
            "A banana milkshake is a thick, sweet, and creamy beverage made by blending ripe bananas with chilled milk, often combined with vanilla ice cream, sweeteners, or flavorings like cinnamon and vanilla extract.",
            13.00m, "0f05ced75a14442780f2a30551981cdd.jpg", new[] { "Dairy" })
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
