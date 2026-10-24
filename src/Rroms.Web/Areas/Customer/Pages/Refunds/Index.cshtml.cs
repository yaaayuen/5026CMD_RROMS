using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Customer.Pages.Refunds;
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(IBillingService billing) : PageModel
{
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public RefundStatus? Status { get; set; }
    public RefundList Refunds { get; private set; } = new();
    public async Task OnGetAsync() { Refunds = await billing.GetRefundsAsync(User.GetUserId(), PageNumber, Status); }
}
