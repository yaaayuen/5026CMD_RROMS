using System.Data.Common;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Rroms.Web.Data;

namespace Rroms.Tests.Infrastructure;

/// <summary>
/// Starts the real Three Grills application in memory for integration tests,
/// with SQL Server swapped for an in-memory SQLite database.
/// </summary>
/// <remarks>
/// Based on Microsoft's documented pattern "Integration tests in ASP.NET Core".
/// Each factory instance gets its own empty database, so tests cannot affect each other.
/// </remarks>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Email of the administrator the seeder creates in tests.</summary>
    public const string AdminEmail = "admin@threegrills.test";

    /// <summary>Password that satisfies the Identity policy (10+ chars, upper, lower, digit, symbol).</summary>
    public const string AdminPassword = "Test!Admin2026";

    /// <summary>
    /// The SQLite in-memory database only exists while this connection stays open,
    /// so the factory keeps it open until it is disposed.
    /// </summary>
    private readonly SqliteConnection _connection;

    /// <summary>Opens the private in-memory database for this factory.</summary>
    public TestWebApplicationFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <summary>
    /// Adjusts the app's configuration and services before it starts.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" makes DatabaseInitializer use EnsureCreated instead of SQL Server migrations.
        builder.UseEnvironment(DatabaseInitializer.TestingEnvironment);

        builder.ConfigureAppConfiguration((context, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = AdminEmail,
                ["BootstrapAdmin:Password"] = AdminPassword
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the SQL Server registration made in Program.cs.
            // Since EF Core 9 the provider is registered through IDbContextOptionsConfiguration,
            // so both service types must be removed before adding SQLite.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            // Share the single open connection with every DbContext the app creates.
            services.AddSingleton<DbConnection>(_connection);
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                DbConnection connection = serviceProvider.GetRequiredService<DbConnection>();
                options.UseSqlite(connection);
            });
        });
    }

    /// <summary>Closes the in-memory database when the factory is disposed.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}