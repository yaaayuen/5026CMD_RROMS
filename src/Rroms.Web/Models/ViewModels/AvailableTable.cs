namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One free table in the search results (WL-2). Only the details the customer needs to choose.
/// </summary>
public class AvailableTable
{
    /// <summary>The table's ID, sent back when the customer presses Book.</summary>
    public int DiningTableId { get; set; }

    /// <summary>Table label, e.g. "T03".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Name of the area the table is in, e.g. "Patio".</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>Number of seats.</summary>
    public int Capacity { get; set; }
}