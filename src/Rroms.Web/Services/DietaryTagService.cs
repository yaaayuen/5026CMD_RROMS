using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// Administrators add new dietary tags here (YY-10). Owner: YY.
/// Tags are only added, never deleted, because customers and dishes may already use them.
/// </summary>
public class DietaryTagService : IDietaryTagService
{
    //Shown when the name is already used, ignoring upper and lower case
    public const string DuplicateNameMessage = "A dietary tag with this name already exists.";

    //Shown when the name is empty or only spaces
    public const string NameRequiredMessage = "Enter a name for the dietary tag.";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<DietaryTagService> _logger;

    //Receives the database context and a logger from dependency injection
    public DietaryTagService(ApplicationDbContext context, ILogger<DietaryTagService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DietaryTag>> GetAllAsync()
    {
        return await _context.DietaryTags
            .AsNoTracking()
            .OrderBy(tag => tag.DisplayOrder)
            .ToListAsync();
    }

    public async Task<ServiceResult> AddAsync(string name)
    {
        // Rule 1: the service checks the name itself, even though the page also validates it.
        string cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0)
        {
            return ServiceResult.Failure(NameRequiredMessage);
        }

        if (cleanName.Length > DietaryTag.NameMaxLength)
        {
            return ServiceResult.Failure($"The name can be at most {DietaryTag.NameMaxLength} characters.");
        }

        // Rule 2: "dairy" and "Dairy" are the same tag to a customer, so compare without case.
        string lowerName = cleanName.ToLower();
        bool alreadyExists = await _context.DietaryTags.AnyAsync(tag => tag.Name.ToLower() == lowerName);
        if (alreadyExists)
        {
            return ServiceResult.Failure(DuplicateNameMessage);
        }

        // Rule 3: new tags go at the end of the list, so existing checkboxes do not move.
        int lastOrder = await _context.DietaryTags.MaxAsync(tag => (int?)tag.DisplayOrder) ?? 0;

        var tag = new DietaryTag
        {
            Name = cleanName,
            DisplayOrder = lastOrder + 1
        };
        _context.DietaryTags.Add(tag);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two administrators added the same name at the same moment: the unique index refused the second one.
            return ServiceResult.Failure(DuplicateNameMessage);
        }

        _logger.LogInformation("Dietary tag {TagId} \"{TagName}\" added.", tag.Id, tag.Name);
        return ServiceResult.Success();
    }
}
