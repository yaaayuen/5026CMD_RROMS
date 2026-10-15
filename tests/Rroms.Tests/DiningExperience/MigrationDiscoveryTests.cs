using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Rroms.Web.Data;

namespace Rroms.Tests.DiningExperience;

public class MigrationDiscoveryTests
{
    [Fact]
    public void MigrationIdsAreUnique()
    {
        var ids = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Migration).IsAssignableFrom(t))
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id != null).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void SqlServerDiscoversTheExistingDiningMigrationOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationDiscoveryOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        // GetMigrations reads the assembly without opening a database connection.
        var migrations = context.Database.GetMigrations().ToList();
        Assert.Single(migrations, id => id == "20261013005216_S4_Alex_AnnouncementsFeedback");
    }
}
