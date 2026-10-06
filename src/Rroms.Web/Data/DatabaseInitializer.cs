using Microsoft.EntityFrameworkCore;

namespace Rroms.Web.Data;

/// <summary>
/// Prepares the database when the application starts: schema first, then seed data.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Name of the environment used by the automated tests (see Rroms.Tests).
    /// </summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Brings the database schema up to date and seeds roles and the first administrator.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="environment">Tells us whether we are running tests, development or production.</param>
    /// <param name="configuration">Reads Database:ApplyMigrationsOnStartup.</param>
    public static async Task InitializeAsync(
        IServiceProvider services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        // DbContext is registered as "scoped" (one per web request). Outside a request we must
        // create our own scope, and dispose it when finished.
        using IServiceScope scope = services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (environment.IsEnvironment(TestingEnvironment))
        {
            // Tests use an in-memory SQLite database. Our migrations are written for SQL Server,
            // so the test database is built directly from the current model instead.
            await context.Database.EnsureCreatedAsync();
        }
        else if (configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            // Development convenience: every teammate gets the latest schema just by running the app.
            // In production this is off, and migrations are applied deliberately with a script.
            await context.Database.MigrateAsync();
        }

        DatabaseSeeder seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }
}