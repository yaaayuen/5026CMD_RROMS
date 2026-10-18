using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>All four DA-3 reports for one resolved range; totals derive from the reported rows.</summary>
public class ReportDashboard
{
    public ReportRange Range { get; set; } = new ReportRange();
    public IReadOnlyList<ReservationStatusReportRow> Reservations { get; set; } = Array.Empty<ReservationStatusReportRow>();
    public IReadOnlyList<SalesMethodReportRow> SalesMethods { get; set; } = Array.Empty<SalesMethodReportRow>();
    public IReadOnlyList<DailySalesReportRow> DailySales { get; set; } = Array.Empty<DailySalesReportRow>();
    public IReadOnlyList<PopularItemReportRow> PopularItems { get; set; } = Array.Empty<PopularItemReportRow>();
    public IReadOnlyList<CustomerActivityReportRow> Customers { get; set; } = Array.Empty<CustomerActivityReportRow>();
    public int ReservationCount { get { return Reservations.Sum(r => r.Count); } }
    public int GuestCount { get { return Reservations.Sum(r => r.Guests); } }
    public int PaidCount { get { return SalesMethods.Sum(r => r.Count); } }
    public decimal PaidTotal { get { return SalesMethods.Sum(r => r.Amount); } }
    public decimal DemoPaidTotal { get { return SalesMethods.Where(r => r.Method == PaymentMethod.SimulatedOnline).Sum(r => r.Amount); } }
    public decimal NonDemoPaidTotal { get { return PaidTotal - DemoPaidTotal; } }
}
