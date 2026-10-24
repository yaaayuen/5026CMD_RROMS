using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>DA-4 formats DA-3 data without duplicating queries or payment rules.</summary>
public class ReportExportService : IReportExportService
{
    private readonly IReportService _reports;
    private const int PdfMargin = 28;
    private const int PdfFontSize = 9;
    /// <summary>Receives the existing report service.</summary>
    public ReportExportService(IReportService reports) { _reports = reports; }
    /// <inheritdoc />
    public async Task<ServiceResult<ReportExportFile>> ExportAsync(ReportFilter filter, ReportExportKind kind, ReportExportFormat format)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(format))
        {
            return ServiceResult<ReportExportFile>.Failure("Choose a valid report and export format.");
        }
        var result = await _reports.GetReportAsync(filter);
        if (!result.Succeeded) { return ServiceResult<ReportExportFile>.Failure(result.Errors.ToArray()); }
        var report = result.Value!;
        var tables = BuildTables(report, kind);
        string title = "Three Grills - " + tables[0].Title;
        string range = $"Malaysia dates: {report.Range.From:yyyy-MM-dd} to {report.Range.To:yyyy-MM-dd} (inclusive)";
        string note = GetNote(kind);
        byte[] bytes;
        string extension;
        string contentType;
        switch (format)
        {
            case ReportExportFormat.Csv:
                bytes = WriteCsv(tables, title, range, note);
                extension = "csv"; contentType = "text/csv; charset=utf-8";
                break;
            case ReportExportFormat.Excel:
                bytes = WriteExcel(tables, title, range, note);
                extension = "xlsx"; contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
            default:
                bytes = WritePdf(tables, title, range, note);
                extension = "pdf"; contentType = "application/pdf";
                break;
        }
        return ServiceResult<ReportExportFile>.Success(new ReportExportFile
        {
            Content = bytes, ContentType = contentType,
            FileName = $"ThreeGrills-{kind}-{report.Range.From:yyyyMMdd}-{report.Range.To:yyyyMMdd}.{extension}"
        });
    }
    private static string GetNote(ReportExportKind kind)
    {
        switch (kind)
        {
            case ReportExportKind.Reservations:
                return "Reservation start date; all statuses. Guests are booked guests, not attendance or occupancy.";
            case ReportExportKind.Sales:
                return "Gross Paid payments by payment date; Unpaid and Voided excluded. Refunds use completion date; net = gross minus completed refunds. Pending/rejected requests are excluded. Item/customer tables remain gross. ";
            case ReportExportKind.PopularItems:
                return "Orders paid in this period; quantity rank with item ID tie-break. Current names, saved unit prices. Line values may differ from payment amounts. ";
            default:
                return "Reservations use start date; orders use placement date (all statuses); payments use payment date (Paid only). Independent activities, current names; includes inactive accounts. ";
        }
    }
    private static List<ReportExportTable> BuildTables(ReportDashboard r, ReportExportKind kind)
    {
        var tables = new List<ReportExportTable>();
        switch (kind)
        {
            case ReportExportKind.Reservations:
                var bookings = new ReportExportTable { Title = "Reservations", Headers = new[] { "Status", "Reservations", "Guests" } };
                foreach (var x in r.Reservations) { bookings.Rows.Add(new object[] { x.Status.ToString(), x.Count, x.Guests }); }
                bookings.Rows.Add(new object[] { "Total", r.ReservationCount, r.GuestCount });
                tables.Add(bookings);
                break;
            case ReportExportKind.Sales:
                var totals = new ReportExportTable { Title = "Sales totals", Headers = new[] { "Paid count", "Gross RM" } };
                totals.Rows.Add(new object[] { r.PaidCount, r.PaidTotal });
                tables.Add(totals);
                var methods = new ReportExportTable { Title = "Payment methods", Headers = new[] { "Payment method", "Count", "Amount RM" } };
                foreach (var x in r.SalesMethods) { methods.Rows.Add(new object[] { x.Method.ToString(), x.Count, x.Amount }); }
                tables.Add(methods);
                var daily = new ReportExportTable { Title = "Daily sales", Headers = new[] { "Date", "Payments", "Gross RM" } };
                foreach (var x in r.DailySales) { daily.Rows.Add(new object[] { x.Date, x.Count, x.PaidAmount }); }
                tables.Add(daily);
                var refundTotals = new ReportExportTable { Title = "Refund and net totals", Headers = new[] { "Measure", "Gross RM" } };
                refundTotals.Rows.Add(new object[] { "Completed refunds", r.RefundTotal });
                refundTotals.Rows.Add(new object[] { "Net payments", r.NetTotal });
                tables.Add(refundTotals);
                var refunds = new ReportExportTable { Title = "Completed refunds", Headers = new[] { "Receipt", "Customer", "Completed Malaysia", "Method", "Amount RM", "Reason", "Recorded by" } };
                foreach(var x in r.Refunds) refunds.Rows.Add(new object[] { $"R-{x.PaymentId:D4}", x.CustomerName,
                    MalaysiaTime.FromUtc(x.ProcessedAtUtc!.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    x.Method.ToString(), x.Amount, x.DecisionReason ?? x.Reason, x.ProcessedById ?? "" });
                tables.Add(refunds);
                break;
            case ReportExportKind.PopularItems:
                var items = new ReportExportTable { Title = "Popular items", Headers = new[] { "Rank", "Item ID", "Item", "Quantity", "Line value RM" } };
                foreach (var x in r.PopularItems) { items.Rows.Add(new object[] { x.Rank, x.MenuItemId, x.Name, x.Quantity, x.LineValue }); }
                tables.Add(items);
                break;
            default:
                var customers = new ReportExportTable { Title = "Customer activity", Headers = new[] { "Customer ID", "Customer", "Reservations", "Orders", "Payments", "Gross RM" } };
                foreach (var x in r.Customers) { customers.Rows.Add(new object[] { x.CustomerId, x.CustomerName, x.ReservationCount, x.OrderCount, x.PaidCount, x.PaidAmount }); }
                tables.Add(customers);
                break;
        }
        return tables;
    }
    private static string Display(object value)
    {
        if (value is decimal amount) { return amount.ToString("0.00", CultureInfo.InvariantCulture); }
        if (value is DateOnly date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
    private static string CsvCell(object value)
    {
        string text = Display(value);
        // Quote escaping alone does not stop spreadsheet formula execution.
        if (value is string)
        {
            string trimmed = text.TrimStart();
            if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
            {
                text = "'" + text;
            }
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static byte[] WriteCsv(List<ReportExportTable> tables, string title, string range, string note)
    {
        var text = new StringBuilder();
        foreach (string metadata in new[] { title, range, note }) { text.Append(CsvCell(metadata)).Append("\r\n"); }
        foreach (var table in tables)
        {
            text.Append("\r\n").Append(CsvCell(table.Title)).Append("\r\n");
            text.Append(string.Join(",", table.Headers.Select(x => CsvCell(x)))).Append("\r\n");
            foreach (var row in table.Rows) { text.Append(string.Join(",", row.Select(CsvCell))).Append("\r\n"); }
            if (table.Rows.Count == 0) { text.Append(CsvCell("No records in this period.")).Append("\r\n"); }
        }
        // BOM lets Excel detect Unicode customer/item names when opening CSV directly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }
    private static byte[] WriteExcel(List<ReportExportTable> tables, string title, string range, string note)
    {
        using var book = new XLWorkbook();
        var info = book.Worksheets.Add("Report info");
        info.Cell(1, 1).Value = title; info.Cell(2, 1).Value = range; info.Cell(3, 1).Value = note;
        info.Column(1).Width = 100; info.Rows(1, 3).Style.Alignment.WrapText = true;
        info.Row(3).Height = 60; info.Rows(1, 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top; info.Cell(1, 1).Style.Font.Bold = true;
        foreach (var table in tables)
        {
            var sheet = book.Worksheets.Add(table.Title);
            for (int c = 0; c < table.Headers.Length; c++) { sheet.Cell(1, c + 1).Value = table.Headers[c]; }
            for (int r = 0; r < table.Rows.Count; r++)
            {
                for (int c = 0; c < table.Headers.Length; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    object value = table.Rows[r][c];
                    if (value is decimal amount) { cell.Value = amount; cell.Style.NumberFormat.Format = "0.00"; }
                    else if (value is int count) { cell.Value = count; }
                    else if (value is DateOnly date) { cell.Value = date.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; }
                    else { cell.Value = Display(value); } // Explicit string values, never FormulaA1.
                }
            }
            if (table.Rows.Count == 0) { sheet.Cell(2, 1).Value = "No records in this period."; }
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            sheet.Columns(1, table.Headers.Length).Width = 22;
            sheet.RangeUsed()!.Style.Alignment.WrapText = true;
            sheet.SheetView.FreezeRows(1);
        }
        // Start on the report data, so the information sheet is not mistaken for an empty export.
        var firstDataSheet = book.Worksheet(tables[0].Title);
        firstDataSheet.Position = 1;
        firstDataSheet.SetTabActive();
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
    private static byte[] WritePdf(List<ReportExportTable> tables, string title, string range, string note)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape()); page.Margin(PdfMargin);
                page.DefaultTextStyle(style => style.FontSize(PdfFontSize));
                page.Header().Column(column =>
                {
                    column.Item().Text(title).Bold().FontSize(16);
                    column.Item().Text(range);
                });
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Text(note);
                    foreach (var source in tables)
                    {
                        column.Item().Text(source.Title).Bold().FontSize(12);
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (string header in source.Headers) { columns.RelativeColumn(); }
                            });
                            table.Header(header =>
                            {
                                foreach (string label in source.Headers) { header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(label).Bold(); }
                            });
                            foreach (var row in source.Rows)
                            {
                                foreach (object value in row) { table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Display(value)); }
                            }
                        });
                        if (source.Rows.Count == 0) { column.Item().Text("No records in this period."); }
                    }
                });
                page.Footer().AlignRight().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
            });
        }).GeneratePdf();
    }
}
