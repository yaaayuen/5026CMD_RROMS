namespace Rroms.Web.Services;
/// <summary>Internal typed table shared by all three renderers to avoid mismatched figures.</summary>
internal class ReportExportTable
{
    public string Title { get; set; } = string.Empty;
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<object[]> Rows { get; set; } = new List<object[]>();
}
