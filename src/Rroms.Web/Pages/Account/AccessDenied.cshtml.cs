using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Shown when a signed-in user tries to access a page they don't have permission for. This page informs the user that they are not authorized to view the requested resource, providing a clear and user-friendly message about access restrictions.
/// for example, a customer visiting /Admin. Owener: YY (YY-2; used by YY-3).
/// </summary>
public class AccessDeniedModel : PageModel
{
    //Handles GET /Account/AccessDenied. The page is static, no data is needed.
    public void OnGet()
    {
    }
}