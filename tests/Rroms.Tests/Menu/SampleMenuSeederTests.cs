using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Acceptance tests for <see cref="SampleMenuSeeder"/>: the sample menu every teammate gets on start-up (YJ-1).
/// </summary>
/// <remarks>
/// Seed data in every test: the categories Starters, Grills, Pasta, Desserts and Beverages and the dietary tags
/// Animal products, Dairy and Spicy. No menu items are seeded by the database itself.
/// </remarks>
public class SampleMenuSeederTests : IDisposable
{
    private const int GrillsId = 2;
    private const int SampleDishCount = 27;
    private const decimal NormalPrice = 20.00m;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly SampleMenuSeeder _seeder;

    /// <summary>Runs before EVERY test: a brand-new in-memory database.</summary>
    public SampleMenuSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _seeder = new SampleMenuSeeder(_context, NullLogger<SampleMenuSeeder>.Instance);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Reads one dish back with its category and tags.</summary>
    private async Task<MenuItem> LoadDishAsync(string name)
    {
        return await _context.MenuItems
            .Include(item => item.MenuCategory)
            .Include(item => item.DietaryTags)
            .SingleAsync(item => item.Name == name);
    }

    /// <summary>Walks up from the test output folder to the repository root (the folder that holds src/Rroms.Web).</summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder != null)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, "src", "Rroms.Web")))
            {
                return folder.FullName;
            }

            folder = folder.Parent;
        }

        throw new InvalidOperationException("The repository root (a folder with src/Rroms.Web) was not found above " + AppContext.BaseDirectory);
    }

    // ---------------------------------------------------------------------------------------------
    // Empty menu
    // ---------------------------------------------------------------------------------------------

    /// <summary>A teammate with an empty menu gets all the sample dishes.</summary>
    [Fact]
    public async Task Seed_EmptyMenu_AddsAllSampleDishes()
    {
        int added = await _seeder.SeedAsync();

        Assert.Equal(SampleDishCount, added);
        Assert.Equal(SampleDishCount, await _context.MenuItems.CountAsync());
    }

    /// <summary>Each dish lands in the category named in the sample data, with its price and photo.</summary>
    [Fact]
    public async Task Seed_PutsEachDishInItsCategoryWithPriceAndPhoto()
    {
        await _seeder.SeedAsync();

        MenuItem burger = await LoadDishAsync("Smoky BBQ Burger");

        Assert.Equal("Grills", burger.MenuCategory!.Name);
        Assert.Equal(28.00m, burger.Price);
        Assert.Equal("6dc9f1588d454147a16874091f41f329.jpg", burger.ImageFileName);
        Assert.True(burger.IsAvailable);

        MenuItem cola = await LoadDishAsync("Coca-cola");

        Assert.Null(cola.ImageFileName);
    }

    /// <summary>The dietary tags are linked by name.</summary>
    [Fact]
    public async Task Seed_LinksTheDietaryTags()
    {
        await _seeder.SeedAsync();

        MenuItem pasta = await LoadDishAsync("Creamy Gochujang Pasta");
        MenuItem salad = await LoadDishAsync("Salad");

        Assert.Equal(new[] { "Dairy", "Spicy" }, pasta.DietaryTags.Select(t => t.Name).OrderBy(n => n).ToArray());
        Assert.Empty(salad.DietaryTags);
    }

    /// <summary>Codes follow the same rule as the admin page: category letter plus a number, never repeated.</summary>
    [Fact]
    public async Task Seed_GivesEveryDishAUniqueCodeFromItsCategoryLetter()
    {
        await _seeder.SeedAsync();

        List<MenuItem> items = await _context.MenuItems.Include(i => i.MenuCategory).ToListAsync();

        Assert.Equal(items.Count, items.Select(i => i.Code).Distinct().Count());

        foreach (MenuItem item in items)
        {
            Assert.NotNull(item.Code);
            Assert.StartsWith(item.MenuCategory!.Name.Substring(0, 1), item.Code);
        }

        Assert.Equal("S03", (await LoadDishAsync("Garlic Bread")).Code);
        Assert.Equal("G01", (await LoadDishAsync("Smoky BBQ Burger")).Code);
        Assert.Equal("P01", (await LoadDishAsync("Pesto Pasta")).Code);
        Assert.Equal("B01", (await LoadDishAsync("Orange Juice")).Code);
    }

    // ---------------------------------------------------------------------------------------------
    // Running again, and menus that already have dishes
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starting the app a second time must not add the dishes again.</summary>
    [Fact]
    public async Task Seed_RunTwice_AddsNothingTheSecondTime()
    {
        await _seeder.SeedAsync();

        int addedAgain = await _seeder.SeedAsync();

        Assert.Equal(0, addedAgain);
        Assert.Equal(SampleDishCount, await _context.MenuItems.CountAsync());
    }

    /// <summary>A dish the teammate already has (even typed in lower case) is left alone.</summary>
    [Fact]
    public async Task Seed_DishWithSameNameAlreadyExists_IsSkippedAndNotChanged()
    {
        _context.MenuItems.Add(new MenuItem
        {
            Code = "D01",
            Name = "tiramisu",
            Description = "My own tiramisu",
            MenuCategoryId = 4,
            Price = 99.00m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        int added = await _seeder.SeedAsync();

        Assert.Equal(SampleDishCount - 1, added);

        MenuItem mine = await LoadDishAsync("tiramisu");

        Assert.Equal(99.00m, mine.Price);
        Assert.Equal("My own tiramisu", mine.Description);
        Assert.Equal(1, await _context.MenuItems.CountAsync(i => i.Name.ToLower() == "tiramisu"));
    }

    /// <summary>New codes carry on after the codes the teammate already has.</summary>
    [Fact]
    public async Task Seed_ContinuesNumberingAfterExistingCodes()
    {
        _context.MenuItems.Add(new MenuItem
        {
            Code = "G05",
            Name = "Steak",
            Description = "Already on my menu",
            MenuCategoryId = GrillsId,
            Price = NormalPrice,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await _seeder.SeedAsync();

        Assert.Equal("G06", (await LoadDishAsync("Smoky BBQ Burger")).Code);
    }

    // ---------------------------------------------------------------------------------------------
    // Missing categories and tags must not stop the application
    // ---------------------------------------------------------------------------------------------

    /// <summary>If a tag was removed, the dish is still added, just without that tag.</summary>
    [Fact]
    public async Task Seed_MissingTag_AddsTheDishWithoutIt()
    {
        DietaryTag spicy = await _context.DietaryTags.SingleAsync(t => t.Name == "Spicy");
        _context.DietaryTags.Remove(spicy);
        await _context.SaveChangesAsync();

        int added = await _seeder.SeedAsync();

        Assert.Equal(SampleDishCount, added);

        MenuItem pasta = await LoadDishAsync("Creamy Gochujang Pasta");

        Assert.Equal(new[] { "Dairy" }, pasta.DietaryTags.Select(t => t.Name).ToArray());
    }

    /// <summary>If a category does not exist, its dishes are skipped and nothing crashes.</summary>
    [Fact]
    public async Task Seed_MissingCategory_SkipsOnlyThoseDishes()
    {
        MenuCategory beverages = await _context.MenuCategories.SingleAsync(c => c.Name == "Beverages");
        _context.MenuCategories.Remove(beverages);
        await _context.SaveChangesAsync();

        int added = await _seeder.SeedAsync();

        Assert.Equal(SampleDishCount - 6, added);
        Assert.False(await _context.MenuItems.AnyAsync(i => i.Name == "Orange Juice"));
    }

    // ---------------------------------------------------------------------------------------------
    // The sample data itself
    // ---------------------------------------------------------------------------------------------

    /// <summary>The sample list obeys the same rules as the admin page, so the database accepts every row.</summary>
    [Fact]
    public void SampleData_ObeysTheMenuRules()
    {
        Assert.Equal(SampleDishCount, SampleMenuSeeder.Dishes.Count);
        Assert.Equal(
            SampleMenuSeeder.Dishes.Count,
            SampleMenuSeeder.Dishes.Select(d => d.Name.ToLowerInvariant()).Distinct().Count());

        foreach (SampleMenuSeeder.SampleDish dish in SampleMenuSeeder.Dishes)
        {
            Assert.InRange(dish.Name.Length, 1, MenuItem.NameMaxLength);
            Assert.InRange(dish.Description.Length, 1, MenuItem.DescriptionMaxLength);
            Assert.True(dish.Price > 0);
        }
    }

    /// <summary>
    /// Every photo named in the sample data must be a committed file, otherwise teammates would see broken images.
    /// This test fails if a photo was added to the list but not committed (or is still ignored by git).
    /// </summary>
    [Fact]
    public void SampleData_EveryPhotoFileExistsInTheRepository()
    {
        string photoFolder = Path.Combine(
            FindRepositoryRoot(), "src", "Rroms.Web", "wwwroot", "uploads", "menu");

        foreach (SampleMenuSeeder.SampleDish dish in SampleMenuSeeder.Dishes)
        {
            if (dish.ImageFileName == null)
            {
                continue;
            }

            string file = Path.Combine(photoFolder, dish.ImageFileName);

            Assert.True(File.Exists(file), $"Photo for {dish.Name} is missing: {file}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tests keep an empty menu
    // ---------------------------------------------------------------------------------------------

    /// <summary>The real application seeds the sample menu when it starts, but never in the Testing environment.</summary>
    [Fact]
    public async Task Application_InTestingEnvironment_StartsWithAnEmptyMenu()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await context.MenuItems.CountAsync());
    }
}