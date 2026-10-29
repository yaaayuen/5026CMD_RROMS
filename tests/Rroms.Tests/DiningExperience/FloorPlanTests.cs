using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.DiningExperience;

/// <summary>
/// Floor plan rules and saving (Alex, Part 20). Each test gets a fresh in-memory database with the seed layout:
/// Main Hall 24x15 boxes with the bar (0,0 3x8) and kitchen (18,0 6x5); T05 (6 seats) at (12,7).
/// </summary>
public class FloorPlanTests : IDisposable
{
    private const int MainHallId = 1;
    private const int PatioId = 2;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly FloorPlanService _service;

    public FloorPlanTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new FloorPlanService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    //================================================== Rules ==================================================//

    [Theory]
    [InlineData(2, 0, 3, 3)]
    [InlineData(4, 0, 4, 4)]
    [InlineData(6, 0, 5, 4)]
    [InlineData(8, 0, 6, 4)]
    [InlineData(8, 90, 4, 6)]
    public void Footprint_DependsOnSeatsAndRotation(int seats, int rotation, int width, int height)
    {
        Assert.Equal((width, height), FloorPlanRules.Footprint(seats, rotation));
    }

    [Fact]
    public void Problems_AllowTablesAgainstWallsAndFeatures_WithOneBoxBetweenTables()
    {
        var kitchen = new FloorPlanRules.Obstacle("Kitchen", new FloorPlanRules.Box(18, 0, 6, 5));
        var tables = new[]
        {
            new FloorPlanRules.PlacedTable("A", 6, 13, 5, 0),   // just under the kitchen's left edge
            new FloorPlanRules.PlacedTable("B", 6, 19, 5, 0)    // against the right wall, one box from A
        };

        Assert.Empty(FloorPlanRules.Problems("Main Hall", 24, 15, new[] { kitchen }, tables));
    }

    [Fact]
    public void Problems_ReportTouchingTables_OverlappedFeatures_AndTheEdge()
    {
        var bar = new FloorPlanRules.Obstacle("Bar", new FloorPlanRules.Box(0, 0, 3, 8));
        var tables = new[]
        {
            new FloorPlanRules.PlacedTable("A", 4, 2, 2, 0),    // overlaps the bar
            new FloorPlanRules.PlacedTable("B", 4, 6, 2, 0),    // touches A: no clear box
            new FloorPlanRules.PlacedTable("C", 2, 22, 13, 0)   // runs past the bottom-right corner
        };

        List<string> problems = FloorPlanRules.Problems("Main Hall", 24, 15, new[] { bar }, tables);

        Assert.Contains("Table A overlaps the bar.", problems);
        Assert.Contains("Tables A and B need at least one clear box between them.", problems);
        Assert.Contains("Table C goes past the edge of Main Hall.", problems);
    }

    [Fact]
    public void FirstFreeSpot_SkipsTheBarAndOtherTables()
    {
        var bar = new FloorPlanRules.Obstacle("Bar", new FloorPlanRules.Box(0, 0, 3, 8));
        var taken = new[] { new FloorPlanRules.Box(3, 0, 4, 4) };

        (int x, int y) = FloorPlanRules.FirstFreeSpot(2, 24, 15, new[] { bar }, taken);

        Assert.Equal((8, 0), (x, y));
    }

    //================================================== Reading the plan ==================================================//

    [Fact]
    public async Task GetFloorPlan_ReturnsSeededAreasFeaturesAndPositions()
    {
        FloorPlanView plan = await _service.GetFloorPlanAsync(includeInactive: false);

        FloorAreaView hall = plan.Areas.Single(a => a.Id == MainHallId);
        Assert.Equal((24, 15), (hall.Width, hall.Height));
        Assert.Contains(hall.Features, f => f.Label == "Kitchen" && f.Kind == FloorFeatureKinds.Block);
        FloorTableView t05 = hall.Tables.Single(t => t.Label == "T05");
        Assert.Equal((12, 7, 0), (t05.X, t05.Y, t05.Rotation));
        Assert.Equal(2, plan.Areas.Single(a => a.Id == PatioId).Tables.Count);
    }

    [Fact]
    public async Task GetFloorPlan_PlacesATableWithoutAPosition_InAFreeSpot()
    {
        _context.DiningTables.Add(new DiningTable { Label = "P03", Capacity = 2, DiningAreaId = PatioId, IsActive = true });
        await _context.SaveChangesAsync();

        FloorAreaView patio = (await _service.GetFloorPlanAsync(false)).Areas.Single(a => a.Id == PatioId);
        FloorTableView p03 = patio.Tables.Single(t => t.Label == "P03");
        var others = patio.Tables.Where(t => t.Label != "P03")
            .Select(t => FloorPlanRules.BoxOf(new FloorPlanRules.PlacedTable(t.Label, t.Capacity, t.X, t.Y, t.Rotation))).ToList();
        var box = FloorPlanRules.BoxOf(new FloorPlanRules.PlacedTable("P03", 2, p03.X, p03.Y, 0));

        Assert.True(p03.Y >= 1, "must not sit on the planters along the top");
        Assert.DoesNotContain(others, o => FloorPlanRules.Overlaps(box, o, FloorPlanRules.Gap));
    }

    [Fact]
    public async Task GetFloorPlan_WithoutInactive_LeavesOutInactiveTables()
    {
        DiningTable t06 = await _context.DiningTables.SingleAsync(t => t.Label == "T06");
        t06.IsActive = false;
        await _context.SaveChangesAsync();

        FloorPlanView plan = await _service.GetFloorPlanAsync(includeInactive: false);

        Assert.DoesNotContain(plan.Areas.SelectMany(a => a.Tables), t => t.Label == "T06");
    }

    //================================================== Saving ==================================================//

    [Fact]
    public async Task SaveLayout_MovesTurnsAndRenamesTables_AndAddsNewOnes()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        FloorTableInput t05 = tables.Single(t => t.Label == "T05");
        t05.X = 14; t05.Y = 7; t05.Label = "T05A";
        tables.Single(t => t.Label == "T06").Rotation = 90;
        tables.Single(t => t.Label == "T06").X = 20;
        tables.Single(t => t.Label == "T06").Y = 9;
        tables.Add(new FloorTableInput { Id = 0, Label = "T07", Capacity = 2, X = 14, Y = 12, IsActive = true });

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        FloorAreaView hall = (await _service.GetFloorPlanAsync(true)).Areas.Single(a => a.Id == MainHallId);
        Assert.Equal((14, 7), (hall.Tables.Single(t => t.Label == "T05A").X, hall.Tables.Single(t => t.Label == "T05A").Y));
        Assert.Equal(90, hall.Tables.Single(t => t.Label == "T06").Rotation);
        Assert.Contains(hall.Tables, t => t.Label == "T07" && t.X == 14 && t.Y == 12);
        Assert.True(await _context.DiningTables.AnyAsync(t => t.Label == "T07" && t.DiningAreaId == MainHallId));
    }

    [Fact]
    public async Task SaveLayout_RefusesTablesTooCloseTogether_AndChangesNothing()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        FloorTableInput t04 = tables.Single(t => t.Label == "T04");
        t04.X = 8;   // T03 ends at x = 8, so the tables would touch

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.False(result.Succeeded);
        Assert.Contains("Tables T03 and T04 need at least one clear box between them.", result.Errors);
        Assert.Equal(9, (await _context.TablePlacements.SingleAsync(p => p.DiningTableId == t04.Id)).X);
    }

    [Fact]
    public async Task SaveLayout_RefusesATableOnTheKitchen()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        FloorTableInput t06 = tables.Single(t => t.Label == "T06");
        t06.Y = 3;

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.Contains("Table T06 overlaps the kitchen.", result.Errors);
    }

    [Fact]
    public async Task SaveLayout_RefusesToDropATable()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        tables.RemoveAll(t => t.Label == "T01");

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.Contains(FloorPlanService.MissingTableMessage, result.Errors);
    }

    [Fact]
    public async Task SaveLayout_RefusesALabelUsedInAnotherArea()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        tables.Single(t => t.Label == "T01").Label = "p01";

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.Contains("The label p01 is used by another table.", result.Errors);
    }

    [Fact]
    public async Task SaveLayout_RefusesATableFromAnotherArea()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        DiningTable p01 = await _context.DiningTables.SingleAsync(t => t.Label == "P01");
        tables.Add(new FloorTableInput { Id = p01.Id, Label = "P01", Capacity = 2, X = 14, Y = 11, IsActive = true });

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.Contains(FloorPlanService.ForeignTableMessage, result.Errors);
    }

    [Fact]
    public async Task SaveLayout_AllowsAnInactiveTableToOverlap()
    {
        List<FloorTableInput> tables = await CurrentLayoutAsync(MainHallId);
        FloorTableInput t02 = tables.Single(t => t.Label == "T02");
        t02.X = 4; t02.IsActive = false;   // sits on top of T01, but is not in use

        ServiceResult result = await _service.SaveAreaLayoutAsync(MainHallId, tables);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.False((await _context.DiningTables.SingleAsync(t => t.Label == "T02")).IsActive);
    }

    private async Task<List<FloorTableInput>> CurrentLayoutAsync(int areaId)
    {
        FloorAreaView area = (await _service.GetFloorPlanAsync(true)).Areas.Single(a => a.Id == areaId);
        return area.Tables.Select(t => new FloorTableInput
        {
            Id = t.Id, Label = t.Label, Capacity = t.Capacity, X = t.X, Y = t.Y, Rotation = t.Rotation, IsActive = t.IsActive
        }).ToList();
    }
}
