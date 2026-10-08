using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Restaurant;

/// <summary>
/// Unit tests for the WL-1 restaurant setup rules in <see cref="RestaurantService"/>.
/// </summary>
/// <remarks>
/// Every test follows Arrange (prepare data) - Act (call the method) - Assert (check the result).
/// Seed data available in every test: 7 opening days 11:00-22:00, areas "Main Hall" (Id 1) and
/// "Patio" (Id 2), tables T01-T06 in Main Hall and P01-P02 in Patio, and 5 menu categories.
/// </remarks>
public class RestaurantServiceTests : IDisposable
{
    /// <summary>Id of the seeded "Main Hall" area.</summary>
    private const int MainHallId = 1;

    /// <summary>Id of the seeded "Patio" area.</summary>
    private const int PatioId = 2;

    /// <summary>Id of the seeded table "T01".</summary>
    private const int TableT01Id = 1;

    /// <summary>A Monday in the sprint (12 Oct 2026).</summary>
    private static readonly DateOnly SampleMonday = new DateOnly(2026, 10, 12);

    /// <summary>A Tuesday in the sprint (13 Oct 2026).</summary>
    private static readonly DateOnly SampleTuesday = new DateOnly(2026, 10, 13);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly RestaurantService _service;

    /// <summary>
    /// Runs before EVERY test: creates a brand-new in-memory database, so tests never affect
    /// each other (test isolation) and never touch the real SQL Server database.
    /// </summary>
    public RestaurantServiceTests()
    {
        // The in-memory SQLite database lives only while this connection is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);

        // Builds every table from the code, including the WL-1 seed data.
        _context.Database.EnsureCreated();

        _service = new RestaurantService(_context);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    //================================================== 1. Restaurant info: profile ==================================================//

    [Fact]
    public async Task GetProfile_ReturnsSeededRestaurant()
    {
        // Act
        RestaurantProfile profile = await _service.GetProfileAsync();

        // Assert
        Assert.Equal("Three Grills", profile.Name);
    }

    [Fact]
    public async Task UpdateProfile_WithInvalidEmail_IsRejected()
    {
        // Arrange
        var input = new RestaurantProfileInput
        {
            Name = "Three Grills",
            Address = "1 Example Street",
            Phone = "+60 3-1234 5678",
            Email = "not-an-email"
        };

        // Act
        ServiceResult result = await _service.UpdateProfileAsync(input);

        // Assert
        Assert.False(result.Succeeded);
    }

    //================================================== 1. Restaurant info: opening hours and closures ==================================================//

    [Fact]
    public async Task GetOperatingHours_ReturnsSevenDaysMondayFirst()
    {
        // Act
        IReadOnlyList<OperatingHour> hours = await _service.GetOperatingHoursAsync();

        // Assert
        Assert.Equal(7, hours.Count);
        Assert.Equal(DayOfWeek.Monday, hours[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, hours[6].DayOfWeek);
    }

    [Fact]
    public async Task UpdateOperatingHour_OpensAfterCloses_IsRejected()
    {
        // Arrange: opens at 22:00 but closes at 11:00.
        var input = new OperatingHourInput
        {
            DayOfWeek = DayOfWeek.Monday,
            OpensAt = new TimeOnly(22, 0),
            ClosesAt = new TimeOnly(11, 0),
            IsClosed = false
        };

        // Act
        ServiceResult result = await _service.UpdateOperatingHourAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.OpensBeforeClosesMessage, result.Errors);
    }

    [Fact]
    public async Task UpdateOperatingHour_ClosedDay_IgnoresTheTimes()
    {
        // Arrange: the same "wrong" times, but the day is closed, so they do not matter.
        var input = new OperatingHourInput
        {
            DayOfWeek = DayOfWeek.Monday,
            OpensAt = new TimeOnly(22, 0),
            ClosesAt = new TimeOnly(11, 0),
            IsClosed = true
        };

        // Act
        ServiceResult result = await _service.UpdateOperatingHourAsync(input);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AddSpecialClosure_EndBeforeStart_IsRejected()
    {
        // Arrange
        var input = new SpecialClosureInput
        {
            StartDate = new DateOnly(2027, 2, 3),
            EndDate = new DateOnly(2027, 2, 1),
            Reason = "Chinese New Year"
        };

        // Act
        ServiceResult<int> result = await _service.AddSpecialClosureAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.ClosureDatesMessage, result.Errors);
    }

    [Fact]
    public async Task AddSpecialClosure_OverlappingAnotherClosure_IsRejected()
    {
        // Arrange: 1-3 Feb is already closed; the new closure 3-5 Feb shares 3 Feb.
        await _service.AddSpecialClosureAsync(new SpecialClosureInput
        {
            StartDate = new DateOnly(2027, 2, 1),
            EndDate = new DateOnly(2027, 2, 3),
            Reason = "Chinese New Year"
        });

        var overlapping = new SpecialClosureInput
        {
            StartDate = new DateOnly(2027, 2, 3),
            EndDate = new DateOnly(2027, 2, 5),
            Reason = "Staff holiday"
        };

        // Act
        ServiceResult<int> result = await _service.AddSpecialClosureAsync(overlapping);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.ClosureOverlapMessage, result.Errors);
    }

    [Fact]
    public async Task IsClosedOn_DateInsideSpecialClosure_ReturnsTrue()
    {
        // Arrange
        await _service.AddSpecialClosureAsync(new SpecialClosureInput
        {
            StartDate = new DateOnly(2027, 2, 1),
            EndDate = new DateOnly(2027, 2, 3),
            Reason = "Chinese New Year"
        });

        // Act: 2 Feb is in the middle of the closure.
        bool closed = await _service.IsClosedOnAsync(new DateOnly(2027, 2, 2));

        // Assert
        Assert.True(closed);
    }

    [Fact]
    public async Task IsClosedOn_WeekdayMarkedClosed_ReturnsTrue()
    {
        // Arrange: close every Monday.
        await _service.UpdateOperatingHourAsync(new OperatingHourInput
        {
            DayOfWeek = DayOfWeek.Monday,
            OpensAt = new TimeOnly(11, 0),
            ClosesAt = new TimeOnly(22, 0),
            IsClosed = true
        });

        // Act
        bool closed = await _service.IsClosedOnAsync(SampleMonday);

        // Assert
        Assert.True(closed);
    }

    [Fact]
    public async Task IsClosedOn_NormalOpenDay_ReturnsFalse()
    {
        // Act: the seed data opens every day, and there are no closures.
        bool closed = await _service.IsClosedOnAsync(SampleTuesday);

        // Assert
        Assert.False(closed);
    }

    //================================================== 2. Dining layout: areas ==================================================//

    [Fact]
    public async Task CreateDiningArea_DuplicateNameDifferentCase_IsRejected()
    {
        // Arrange: "Main Hall" already exists in the seed data.
        var input = new DiningAreaInput { Name = "main hall", IsActive = true };

        // Act
        ServiceResult<int> result = await _service.CreateDiningAreaAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.DuplicateAreaNameMessage, result.Errors);
    }

    //================================================== 2. Dining layout: tables ==================================================//

    [Theory]
    [InlineData(DiningTable.MinCapacity - 1)]
    [InlineData(DiningTable.MaxCapacity + 1)]
    public async Task CreateTable_WithSeatsOutsideOneToTwenty_IsRejected(int seats)
    {
        // Arrange: runs twice, once with 0 seats and once with 21 seats.
        var input = new DiningTableInput
        {
            Label = "T99",
            Capacity = seats,
            DiningAreaId = MainHallId
        };

        // Act
        ServiceResult<int> result = await _service.CreateTableAsync(input);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateTable_WithValidInput_Succeeds()
    {
        // Arrange
        var input = new DiningTableInput
        {
            Label = "T20",
            Capacity = 4,
            DiningAreaId = MainHallId
        };

        // Act
        ServiceResult<int> result = await _service.CreateTableAsync(input);

        // Assert: saved, and the new table received an Id from the database.
        Assert.True(result.Succeeded);
        Assert.True(result.Value > 0);
    }

    [Fact]
    public async Task CreateTable_WithDuplicateLabelDifferentCase_IsRejected()
    {
        // Arrange: "T01" already exists; "t01" must count as the same label.
        var input = new DiningTableInput
        {
            Label = "t01",
            Capacity = 2,
            DiningAreaId = MainHallId
        };

        // Act
        ServiceResult<int> result = await _service.CreateTableAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.DuplicateTableLabelMessage, result.Errors);
    }

    [Fact]
    public async Task CreateTable_InInactiveArea_IsRejected()
    {
        // Arrange: close the Patio, then try to add a table there.
        await _service.UpdateDiningAreaAsync(PatioId, new DiningAreaInput { Name = "Patio", IsActive = false });

        var input = new DiningTableInput
        {
            Label = "P09",
            Capacity = 2,
            DiningAreaId = PatioId
        };

        // Act
        ServiceResult<int> result = await _service.CreateTableAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.InactiveAreaMessage, result.Errors);
    }

    [Fact]
    public async Task UpdateTable_KeepingItsOwnLabel_Succeeds()
    {
        // Arrange: change only the seats of T01. Its own label must not count as a duplicate.
        var input = new DiningTableInput
        {
            Label = "T01",
            Capacity = 3,
            DiningAreaId = MainHallId,
            IsActive = true
        };

        // Act
        ServiceResult result = await _service.UpdateTableAsync(TableT01Id, input);

        // Assert
        Assert.True(result.Succeeded);
        DiningTable? saved = await _service.GetTableAsync(TableT01Id);
        Assert.NotNull(saved);
        Assert.Equal(3, saved.Capacity);
    }

    [Fact]
    public async Task GetBookableTables_ExcludesInactiveTablesAndTablesInInactiveAreas()
    {
        // Arrange: take T01 out of use, and close the whole Patio (P01, P02).
        await _service.UpdateTableAsync(TableT01Id, new DiningTableInput
        {
            Label = "T01",
            Capacity = 2,
            DiningAreaId = MainHallId,
            IsActive = false
        });
        await _service.UpdateDiningAreaAsync(PatioId, new DiningAreaInput { Name = "Patio", IsActive = false });

        // Act
        IReadOnlyList<DiningTable> bookable = await _service.GetBookableTablesAsync();

        // Assert: 8 seeded tables minus T01, P01 and P02 leaves 5.
        List<string> labels = bookable.Select(t => t.Label).ToList();
        Assert.Equal(5, labels.Count);
        Assert.DoesNotContain("T01", labels);
        Assert.DoesNotContain("P01", labels);
        Assert.DoesNotContain("P02", labels);
    }

    [Fact]
    public async Task Database_DuplicateLabelThatBypassesTheService_IsBlockedByUniqueIndex()
    {
        // Arrange: skip the service and write straight to the database,
        // like a bug or two administrators saving at the same moment.
        _context.DiningTables.Add(new DiningTable
        {
            Label = "T01",
            Capacity = 2,
            DiningAreaId = MainHallId
        });

        // Act + Assert: the unique index on Label refuses the second "T01".
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    //================================================== 3. Menu categories ==================================================//

    [Fact]
    public async Task GetMenuCategories_ReturnsFiveSeededInDisplayOrder()
    {
        // Act
        IReadOnlyList<MenuCategory> categories = await _service.GetMenuCategoriesAsync(includeInactive: false);

        // Assert
        Assert.Equal(5, categories.Count);
        Assert.Equal("Starters", categories[0].Name);
        Assert.Equal("Beverages", categories[4].Name);
    }

    [Fact]
    public async Task CreateMenuCategory_DuplicateName_IsRejected()
    {
        // Arrange: "Grills" already exists in the seed data.
        var input = new MenuCategoryInput { Name = "Grills", DisplayOrder = 60, IsActive = true };

        // Act
        ServiceResult<int> result = await _service.CreateMenuCategoryAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(RestaurantService.DuplicateCategoryNameMessage, result.Errors);
    }
}
