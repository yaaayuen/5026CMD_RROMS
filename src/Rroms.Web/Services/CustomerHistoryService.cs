using Rroms.Web.Data;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="ICustomerHistoryService"/>. Owner: YJ (YJ-5).
/// </summary>
/// <remarks>
/// STEP 1 of test-first development: only the constructor exists and the method throws. The acceptance tests are
/// written against this shape and must FAIL (red). The real code comes next.
/// </remarks>
public class CustomerHistoryService : ICustomerHistoryService
{
    private readonly ApplicationDbContext _context;

    /// <summary>Receives the database from dependency injection.</summary>
    /// <param name="context">The database context.</param>
    public CustomerHistoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<CustomerHistory> GetHistoryAsync(string customerId, int take = ICustomerHistoryService.DefaultTake)
    {
        throw new NotImplementedException("YJ-5 step 2: the rules are written after the tests.");
    }
}
