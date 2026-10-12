using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rroms.Web.Models;

/// <summary>
/// One line of an order: a dish, how many, and the price paid for each. Owner: YJ (YJ-0).
/// </summary>
/// <remarks>
/// <see cref="UnitPrice"/> is a copy of the price at the moment of ordering 
/// (the offer price while an offer ran), 
/// so changing the menu later never changes an old order or bill.
/// </remarks>
public class OrderItem
{
    /// <summary>Longest note a customer can add to one line, e.g. "No onions".</summary>
    public const int NotesMaxLength = 200;

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the order. Deleting an order deletes its lines.</summary>
    public int OrderId { get; set; }

    /// <summary>The order this line belongs to.</summary>
    public Order? Order { get; set; }

    /// <summary>Foreign key to the dish. A dish that has been ordered cannot be deleted.</summary>
    public int MenuItemId { get; set; }

    /// <summary>The dish that was ordered.</summary>
    public MenuItem? MenuItem { get; set; }

    /// <summary>How many of the dish. Must be greater than zero.</summary>
    public int Quantity { get; set; }

    /// <summary>Price in RM for one dish at the moment of ordering.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Optional note for this dish only.</summary>
    [StringLength(NotesMaxLength)]
    public string? Notes { get; set; }

    /// <summary>Price for the whole line: quantity times the unit price. 
    /// Calculated, never stored.</summary>
    [NotMapped]
    public decimal LineTotal
    {
        get { return UnitPrice * Quantity; }
    }
}