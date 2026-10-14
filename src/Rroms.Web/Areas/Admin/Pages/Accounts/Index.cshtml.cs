using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Accounts;

/// <summary>
/// Administrator list of all accounts with search and filters. Owner: YY (YY-5).
/// The whole Admin area is restricted to administrators in Program.cs (YY-3).
/// </summary>

public class IndexModel : PageModel
{
    private readonly IUserAdministrationService _userAdministration;

    //Receives the administration service from dependency injection
    public IndexModel(IUserAdministrationService userAdministration)
    {
        _userAdministration = userAdministration;
    }

    //Text to search for in name, email or phone. Read from the query string
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    //Role filter, or empty for all roles
    [BindProperty(SupportsGet = true)]
    public string? Role { get; set; }

    //"active", "inactive", or empty for both
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    //The accounts that match the filters
    public IReadOnlyList<UserSummary> Users { get; private set; } = new List<UserSummary>();

    //Loads the filtered account list
    public async Task OnGetAsync()
    {
        bool? isActive = null;
        if (Status == "active")
        {
            isActive = true;
        }
        else if (Status == "inactive")
        {
            isActive = false;
        }

        Users = await _userAdministration.SearchUsersAsync(Search, Role, isActive);
    }
}
