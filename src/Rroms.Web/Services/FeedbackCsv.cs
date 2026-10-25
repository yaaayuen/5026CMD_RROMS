using System.Globalization;
using System.Text;
using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Writes the admin feedback list, as filtered on the page, to a CSV file that opens in Excel (Alex).</summary>
public static class FeedbackCsv
{
    public static readonly string[] Headers = { "Date (Malaysia time)", "Reservation", "Customer", "Stars", "Comment",
        "On website", "Flagged by word filter", "Reply", "Replied (Malaysia time)", "Dish ratings" };

    public static byte[] Write(IEnumerable<Feedback> items)
    {
        var text = new StringBuilder();
        text.Append(string.Join(",", Headers.Select(Cell))).Append("\r\n");
        foreach (var item in items)
        {
            string comment = item.CommentRemoved ? "(comment deleted)" : item.Comment;
            string dishes = string.Join("; ", item.DishRatings.OrderBy(d => d.MenuItem?.Name)
                .Select(d => $"{d.MenuItem?.Name ?? "Removed dish"} {d.Rating}/5"));
            string?[] row =
            {
                MalaysiaTime.FromUtc(item.CreatedAtUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                item.ReservationId.ToString(CultureInfo.InvariantCulture),
                item.Reservation?.Customer?.FullName,
                item.Rating.ToString(CultureInfo.InvariantCulture),
                comment,
                item.IsHidden ? "No" : "Yes",
                item.IsFlagged ? "Yes" : "No",
                item.Reply,
                item.RepliedAtUtc.HasValue ? MalaysiaTime.FromUtc(item.RepliedAtUtc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
                dishes
            };
            text.Append(string.Join(",", row.Select(Cell))).Append("\r\n");
        }
        // The byte order mark lets Excel read names and comments in any language.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    /// <summary>Quotes a value, and stops a customer's comment from running as a spreadsheet formula.</summary>
    public static string Cell(string? value)
    {
        string text = value ?? "";
        if (text.Length > 0 && ("=+-@".Contains(text.TrimStart().FirstOrDefault()) || text[0] is '\t' or '\r' or '\n'))
        {
            text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
