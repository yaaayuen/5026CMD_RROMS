using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
using UglyToad.PdfPig;
namespace Rroms.Tests.Exports;
/// <summary>DA-4 tests read actual generated documents and exercise the protected download handler.</summary>
public class ReportExportTests
{
    private static readonly ReportFilter Filter = new ReportFilter { Anchor = new DateOnly(2026, 10, 20) };
    /// <summary>Known Paid cash/demo data yields the same figures in CSV, XLSX and extracted PDF text.</summary>
    [Fact]
    public async Task KnownSales_AllFormatsMatchReport()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dish = await OrderTestData.AddDishAsync(db, "DA4 sales dish");
        var cash = await OrderTestData.AddOrderAsync(db, dish.Id);
        var demo = await OrderTestData.AddOrderAsync(db, dish.Id);
        var voided = await OrderTestData.AddOrderAsync(db, dish.Id);
        DateTime paid = MalaysiaTime.ToUtc(Filter.Anchor.ToDateTime(TimeOnly.MinValue));
        db.Payments.AddRange(
            new Payment { OrderId = cash.Id, StaffId = cash.CustomerId, Method = PaymentMethod.Cash, Amount = 123.45m, PaidAtUtc = paid, Status = PaymentStatus.Paid },
            new Payment { OrderId = demo.Id, StaffId = cash.CustomerId, Method = PaymentMethod.SimulatedOnline, Amount = 23.45m, PaidAtUtc = paid, Status = PaymentStatus.Paid },
            new Payment { OrderId = voided.Id, StaffId = cash.CustomerId, Method = PaymentMethod.Cash, Amount = 999m, PaidAtUtc = paid, Status = PaymentStatus.Voided });
        await db.SaveChangesAsync();
        var report = (await scope.ServiceProvider.GetRequiredService<IReportService>().GetReportAsync(Filter)).Value!;
        Assert.Equal(123.45m, report.PaidTotal);
        var service = scope.ServiceProvider.GetRequiredService<IReportExportService>();
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var result = await service.ExportAsync(Filter, ReportExportKind.Sales, format);
            Assert.True(result.Succeeded);
            var file = result.Value!;
            Assert.Contains("20261020-20261020", file.FileName);
            if (format == ReportExportFormat.Csv)
            {
                string csv = Encoding.UTF8.GetString(file.Content);
                Assert.Contains("\"1\",\"123.45\"", csv);
                Assert.DoesNotContain("Demo RM", csv);
                Assert.Contains("Payment method", csv);
            }
            else if (format == ReportExportFormat.Excel)
            {
                using var book = new XLWorkbook(new MemoryStream(file.Content));
                Assert.Equal("Sales totals", book.Worksheet(1).Name);
                Assert.True(book.Worksheet(1).TabActive);
                var sheet = book.Worksheet("Sales totals");
                Assert.Equal(1, sheet.Cell(2, 1).GetValue<int>());
                Assert.Equal(report.PaidTotal, sheet.Cell(2, 2).GetValue<decimal>());
                Assert.True(sheet.Cell(2, 3).IsEmpty());
                Assert.True(sheet.Cell(2, 4).IsEmpty());
                Assert.Equal(XLDataType.DateTime, book.Worksheet("Daily sales").Cell(2, 1).DataType);
            }
            else
            {
                using var pdf = PdfDocument.Open(file.Content);
                string text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
                Assert.Contains("123.45", text); Assert.DoesNotContain("146.90", text);
                Assert.DoesNotContain("SimulatedOnline", text); Assert.Contains("2026-10-20", text);
            }
            SaveSample(file);
        }
    }
    /// <summary>Round trips quoted Unicode CSV data and ensures formula-like names remain text in XLSX.</summary>
    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    [InlineData("陈, \"Darren\"\r\nGuest")]
    public async Task Names_AreSafeAndPreserved(string name)
    {
        var report = new ReportDashboard
        {
            Range = new ReportRange { From = Filter.Anchor, To = Filter.Anchor },
            Customers = new[] { new CustomerActivityReportRow { CustomerId = "customer-1", CustomerName = name, PaidAmount = 12.50m } }
        };
        var service = new ReportExportService(new FixedReports(report));
        var csv = (await service.ExportAsync(Filter, ReportExportKind.CustomerActivity, ReportExportFormat.Csv)).Value!;
        Assert.Equal(new byte[] { 239, 187, 191 }, csv.Content.Take(3));
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new MemoryStream(csv.Content), Encoding.UTF8);
        parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = false;
        string[]? row = null;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields?[0] == "customer-1") { row = fields; }
        }
        Assert.NotNull(row);
        string expected = name.StartsWith("陈") ? name : "'" + name;
        Assert.Equal(expected.Replace("\r\n", "\n"), row![1].Replace("\r\n", "\n"));
        Assert.Contains(expected.Replace("\"", "\"\""), Encoding.UTF8.GetString(csv.Content)); Assert.Equal("12.50", row[5]);
        var excel = (await service.ExportAsync(Filter, ReportExportKind.CustomerActivity, ReportExportFormat.Excel)).Value!;
        using var book = new XLWorkbook(new MemoryStream(excel.Content));
        var cell = book.Worksheet("Customer activity").Cell(2, 2);
        Assert.False(cell.HasFormula); Assert.Equal(name.Replace("\r\n", "\n"), cell.GetString().Replace("\r\n", "\n"));
    }
    /// <summary>All four report types generate readable downloads, including empty results.</summary>
    [Theory]
    [InlineData(ReportExportKind.Reservations)]
    [InlineData(ReportExportKind.Sales)]
    [InlineData(ReportExportKind.PopularItems)]
    [InlineData(ReportExportKind.CustomerActivity)]
    public async Task EmptyReports(ReportExportKind kind)
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportExportService>();
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var result = await service.ExportAsync(new ReportFilter { Anchor = new DateOnly(2040, 1, 1) }, kind, format);
            Assert.True(result.Succeeded); Assert.NotEmpty(result.Value!.Content);
            if (format == ReportExportFormat.Pdf)
            {
                using var pdf = PdfDocument.Open(result.Value.Content);
                Assert.True(pdf.NumberOfPages >= 1);
            }
        }
    }
    /// <summary>Long data spans pages without losing the final customer or monetary precision under another culture.</summary>
    [Fact]
    public async Task MultiPagePdf_AndCultureIndependentCsv()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var report = new ReportDashboard
        {
            Range = new ReportRange { From = Filter.Anchor, To = Filter.Anchor },
            Customers = Enumerable.Range(1, 100).Select(i => new CustomerActivityReportRow
            {
                CustomerId = "customer-" + i, CustomerName = "Customer with a longer display name " + i,
                ReservationCount = 2, OrderCount = 3, PaidCount = 1, PaidAmount = 123.45m, DemoAmount = 23.45m
            }).ToList()
        };
        var service = new ReportExportService(new FixedReports(report));
        var file = (await service.ExportAsync(Filter, ReportExportKind.CustomerActivity, ReportExportFormat.Pdf)).Value!;
        using var pdf = PdfDocument.Open(file.Content);
        Assert.True(pdf.NumberOfPages > 1);
        Assert.Contains("customer-100", string.Join(" ", pdf.GetPages().Select(x => x.Text)));
        SaveSample(file);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var csv = (await service.ExportAsync(Filter, ReportExportKind.CustomerActivity, ReportExportFormat.Csv)).Value!;
            Assert.Contains("\"123.45\"", Encoding.UTF8.GetString(csv.Content));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
    /// <summary>Invalid dates and enum selections return business failures.</summary>
    [Fact]
    public async Task InvalidSelections()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportExportService>();
        Assert.False((await service.ExportAsync(Filter, (ReportExportKind)999, ReportExportFormat.Csv)).Succeeded);
        Assert.False((await service.ExportAsync(Filter, ReportExportKind.Sales, (ReportExportFormat)999)).Succeeded);
        Assert.False((await service.ExportAsync(new ReportFilter { Period = (ReportPeriod)999 }, ReportExportKind.Sales, ReportExportFormat.Csv)).Succeeded);
    }
    /// <summary>Download handlers enforce the same role restriction as the page.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Staff)]
    public async Task UnauthorizedDownloads(string? role)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        if (role != null)
        {
            string email = $"da4-{Guid.NewGuid():N}@threegrills.test";
            await factory.CreateUserAsync(email, "Grill!Test2026", role);
            await FormHelper.LoginAsync(client, email, "Grill!Test2026");
        }
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var response = await client.GetAsync("/Admin/Reports?handler=Export&kind=Sales&format=" + format);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(role == null ? "/Account/Login" : "AccessDenied", response.Headers.Location!.ToString());
        }
    }
    /// <summary>Administrators get attachments, no-store headers, and validation for malformed input.</summary>
    [Fact]
    public async Task AdministratorDownloads()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var response = await client.GetAsync($"/Admin/Reports?handler=Export&kind=Sales&format={format}&Filter.Period=Weekly&Filter.Anchor=2026-10-20");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Contains("20261019-20261025", response.Content.Headers.ContentDisposition.FileNameStar!);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        foreach (string query in new[] { "kind=999", "format=bad", "Filter.Anchor=bad", "Filter.Period=Custom&Filter.From=2026-10-20&Filter.To=2026-10-01" })
        {
            var response = await client.GetAsync("/Admin/Reports?handler=Export&" + query);
            Assert.Null(response.Content.Headers.ContentDisposition);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        }
        string html = await client.GetStringAsync("/Admin/Reports?Filter.Period=Monthly&Filter.Anchor=2026-02-15");
        Assert.Contains("Download CSV", html); Assert.Contains("Download Excel", html); Assert.Contains("Download PDF", html);
        Assert.Contains("value=\"2026-02-01\"", html); Assert.Contains("value=\"2026-02-28\"", html);
    }
    /// <summary>Non-sales exports preserve status totals, saved line values and independent customer counts.</summary>
    [Theory]
    [InlineData(ReportExportKind.Reservations, "Reservations", "7")]
    [InlineData(ReportExportKind.PopularItems, "Popular items", "37.25")]
    [InlineData(ReportExportKind.CustomerActivity, "Customer activity", "73.25")]
    public async Task PopulatedSections(ReportExportKind kind, string sheetName, string expected)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var report = new ReportDashboard
        {
            Range = new ReportRange { From = Filter.Anchor, To = Filter.Anchor },
            Reservations = new[] { new ReservationStatusReportRow { Status = ReservationStatus.Cancelled, Count = 7, Guests = 14 } },
            PopularItems = new[] { new PopularItemReportRow { Rank = 1, MenuItemId = 42, Name = "Test dish", Quantity = 5, DemoQuantity = 2, LineValue = 37.25m, DemoLineValue = 12.50m } },
            Customers = new[] { new CustomerActivityReportRow { CustomerId = "customer-42", CustomerName = "Test guest", ReservationCount = 7, OrderCount = 3, PaidCount = 2, PaidAmount = 73.25m, DemoAmount = 12.50m } }
        };
        var service = new ReportExportService(new FixedReports(report));
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var file = (await service.ExportAsync(Filter, kind, format)).Value!;
            if (format == ReportExportFormat.Csv) { Assert.Contains(expected, Encoding.UTF8.GetString(file.Content)); }
            else if (format == ReportExportFormat.Excel)
            {
                using var book = new XLWorkbook(new MemoryStream(file.Content));
                var sheet = book.Worksheet(sheetName);
                int column = kind == ReportExportKind.Reservations ? 2 : kind == ReportExportKind.PopularItems ? 5 : 6;
                Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), sheet.Cell(2, column).GetValue<decimal>());
                if (kind == ReportExportKind.Reservations) { Assert.Equal(14, sheet.Cell(3, 3).GetValue<int>()); }
            }
            else
            {
                using var pdf = PdfDocument.Open(file.Content);
                Assert.Contains(expected, string.Join(" ", pdf.GetPages().Select(x => x.Text)));
            }
        }
    }
    private static void SaveSample(ReportExportFile file)
    {
        string? directory = Environment.GetEnvironmentVariable("DA4_SAMPLE_DIR");
        if (directory != null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, file.FileName), file.Content); }
    }
    private class FixedReports : IReportService
    {
        private readonly ReportDashboard _report;
        public FixedReports(ReportDashboard report) { _report = report; }
        public Task<ServiceResult<ReportDashboard>> GetReportAsync(ReportFilter filter)
        {
            return Task.FromResult(ServiceResult<ReportDashboard>.Success(_report));
        }
    }
}
