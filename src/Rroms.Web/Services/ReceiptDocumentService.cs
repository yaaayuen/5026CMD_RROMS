using System.Net;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services.Email;
namespace Rroms.Web.Services;

/// <summary>Formats an already authorised receipt. Callers must use ownership-filtered billing reads.</summary>
public class ReceiptDocumentService(IEmailQueue emails)
{
    public byte[] Pdf(ReceiptSummary receipt) => Document.Create(document => document.Page(page => {
        page.Size(PageSizes.A4); page.Margin(36); page.DefaultTextStyle(s => s.FontSize(10));
        page.Header().Column(c => {
            c.Item().AlignCenter().Text(receipt.Profile.Name).Bold().FontSize(22).FontColor("#a50d44");
            c.Item().AlignCenter().Text(receipt.Profile.Address);
            c.Item().AlignCenter().Text(receipt.Profile.Phone);
            c.Item().PaddingVertical(12).Text($"Receipt {receipt.Number}").Bold().FontSize(16);
        });
        page.Content().Column(c => {
            foreach (string line in Details(receipt)) c.Item().PaddingBottom(4).Text(line);
            foreach(var bill in receipt.Bills) {
                c.Item().PaddingTop(12).Text($"Order #{bill.OrderId}").Bold();
                c.Item().Table(t => {
                    t.ColumnsDefinition(cols => { cols.RelativeColumn(4); cols.RelativeColumn(1); cols.RelativeColumn(2); cols.RelativeColumn(2); });
                    t.Header(h => { foreach(var name in new[]{"Dish", "Qty", "Unit price", "Line total"}) h.Cell().Padding(5).Text(name).Bold(); });
                    foreach(var item in bill.Lines) {
                        t.Cell().Padding(5).Text(item.DishName); t.Cell().Padding(5).Text(item.Quantity.ToString());
                        t.Cell().Padding(5).Text(Money.Format(item.UnitPrice)); t.Cell().Padding(5).Text(Money.Format(item.LineTotal));
                    }
                });
                foreach(var line in Charges(bill.Breakdown)) c.Item().AlignRight().Text(line);
            }
            c.Item().PaddingTop(16).AlignRight().Text($"Recorded total: {Money.Format(receipt.TotalPaid)}").Bold().FontSize(16);
            if(receipt.CashReceived.HasValue) c.Item().AlignRight().Text($"Cash received {Money.Format(receipt.CashReceived.Value)} · Change {Money.Format(receipt.Change!.Value)}");
            c.Item().PaddingTop(16).Text("Thank you for dining with us.");
        });
        page.Footer().AlignCenter().Text(t => { t.Span(receipt.Number + " · Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages(); });
    })).GeneratePdf();

    public bool QueueEmail(ReceiptSummary receipt)
    {
        if(string.IsNullOrWhiteSpace(receipt.CustomerEmail)) return false;
        string E(string value) => WebUtility.HtmlEncode(value);
        var body = new StringBuilder($"<h1>{E(receipt.Profile.Name)}</h1><p>{E(receipt.Profile.Address)}<br>{E(receipt.Profile.Phone)}</p><h2>Receipt {receipt.Number}</h2>");
        foreach(string line in Details(receipt)) body.Append($"<p>{E(line)}</p>");
        foreach(var bill in receipt.Bills) {
            body.Append($"<h3>Order #{bill.OrderId}</h3><table><tr><th>Dish</th><th>Qty</th><th>Unit price</th><th>Line total</th></tr>");
            foreach(var item in bill.Lines) body.Append($"<tr><td>{E(item.DishName)}</td><td>{item.Quantity}</td><td>{Money.Format(item.UnitPrice)}</td><td>{Money.Format(item.LineTotal)}</td></tr>");
            body.Append("</table>");
            foreach(var line in Charges(bill.Breakdown)) body.Append($"<p>{E(line)}</p>");
        }
        body.Append($"<p><strong>Recorded total: {Money.Format(receipt.TotalPaid)}</strong></p>");
        if(receipt.CashReceived.HasValue) body.Append($"<p>Cash received {Money.Format(receipt.CashReceived.Value)} · Change {Money.Format(receipt.Change!.Value)}</p>");
        return emails.TryEnqueue(new EmailMessage(receipt.CustomerEmail, $"Your receipt {receipt.Number}", body.ToString()));
    }
    private static IEnumerable<string> Charges(BillBreakdown b)
    {
        yield return $"Subtotal: {Money.Format(b.Subtotal)}";
        yield return $"Service charge ({b.ServiceChargePercent:0.##}%): {Money.Format(b.ServiceCharge)}";
        yield return $"Service tax ({b.ServiceTaxPercent:0.##}%): {Money.Format(b.ServiceTax)}";
        yield return $"Total: {Money.Format(b.Total)}";
        yield return b.IsLegacy ? "Original total preserved; no additional charges applied." : b.TaxIncludesServiceCharge ? "Tax on subtotal plus service charge." : "Tax on subtotal.";
    }
    private static IEnumerable<string> Details(ReceiptSummary r)
    {
        if(r.Status == Contracts.PaymentStatus.Voided) {
            yield return "VOIDED — this receipt is no longer proof of payment.";
            yield return "Reason: " + r.VoidReason;
            if(r.VoidedAtUtc.HasValue) yield return $"Voided (Malaysia): {MalaysiaTime.FromUtc(r.VoidedAtUtc.Value):dd MMM yyyy, h:mm tt}";
        }
        if(r.Refund is { } refund) {
            yield return "REFUND " + refund.Status.ToString().ToUpperInvariant() + ": " + Money.Format(refund.Amount);
            yield return "Request reason: " + refund.Reason;
            if(refund.ProcessedAtUtc.HasValue) yield return $"Decision (Malaysia): {MalaysiaTime.FromUtc(refund.ProcessedAtUtc.Value):dd MMM yyyy, h:mm tt} — {refund.DecisionReason}";
            if(refund.Status == Contracts.RefundStatus.Completed) yield return "Net paid after refund: " + Money.Format(r.NetPaid);
        }
        yield return $"Database ID: {r.PaymentId}";
        yield return $"Customer: {r.Bill.CustomerName} · Table: {r.Bill.TableLabel}";
        yield return $"Recorded (Malaysia): {MalaysiaTime.FromUtc(r.PaidAtUtc):dd MMM yyyy, h:mm tt}";
        yield return $"Served by: {r.StaffName} · Method: {Money.Method(r.Method)}";
        if(r.Method == Contracts.PaymentMethod.SimulatedOnline) yield return "DEMO ONLY - no real payment processed.";
    }
}
