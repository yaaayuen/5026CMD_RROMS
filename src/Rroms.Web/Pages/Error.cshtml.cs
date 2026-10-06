using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rroms.Web.Pages;

/// <summary>
/// Friendly error page shown outside Development when an unhandled exception occurs.
/// </summary>
/// <remarks>
/// Only a request ID is shown, never the exception details, because stack traces can reveal
/// file paths, SQL or other information useful to an attacker. Staff can match the ID in the logs.
/// </remarks>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    /// <summary>Identifier of the failed request, for matching against the server log.</summary>
    public string? RequestId { get; set; }

    /// <summary>Only show the ID box when there is an ID to show.</summary>
    public bool ShowRequestId
    {
        get { return !string.IsNullOrEmpty(RequestId); }
    }

    /// <summary>Handles GET /Error.</summary>
    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}