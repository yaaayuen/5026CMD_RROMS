using System.ComponentModel.DataAnnotations;

using Rroms.Web.Contracts;

namespace Rroms.Web.Models;

/// <summary>
/// One food order placed by a customer for one of their reservations. Owner: YJ (YJ-0).
/// </summary>
/// <remarks>
/// The table is reached through <see cref="Reservation"/> (no copy of it is kept here).
/// <see cref="CustomerId"/> and <see cref="TotalAmount"/> 
/// ARE deliberate copies, both set once by the
/// order service when the order is placed, inside the same transaction:
/// the customer id makes "my orders" and ownership checks a one-column lookup, and the total lets
/// bills and reports read one column. Alex owns every status change after <see cref="OrderStatus.Placed"/>.
/// </remarks>
public class Order
{
    // Null subtotal identifies a legacy record: never retroactively add charges.
    public decimal? Subtotal { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal ServiceTaxPercent { get; set; }
    public decimal ServiceTax { get; set; }
    public bool TaxIncludesServiceCharge { get; set; }

    /// <summary>Longest note a customer can add to the whole order, e.g. "Allergic to peanuts".</summary>
    public const int NotesMaxLength = 300;

    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the reservation this order belongs to (WL's entity).</summary>
    public int ReservationId { get; set; }

    /// <summary>The reservation this order belongs to. Gives the table and the visit times.</summary>
    public Reservation? Reservation { get; set; }

    /// <summary>
    /// Foreign key to the customer who ordered (an Identity user ID, which is a string).
    /// Always equal to the reservation's customer: the service copies it at placement.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>The customer who ordered.</summary>
    public ApplicationUser? Customer { get; set; }

    /// <summary>Where the order is in the kitchen workflow. New orders start as Placed.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Placed;

    /// <summary>
    /// The total in RM, calculated once by the server from the lines when the order is placed.
    /// Stored (not recalculated) so bills and reports read one column; see the class remarks.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Optional note from the customer for the whole order.</summary>
    [StringLength(NotesMaxLength)]
    public string? Notes { get; set; }

    /// <summary>When the order was placed (UTC).</summary>
    public DateTime PlacedAtUtc { get; set; }

    /// <summary>When the order last changed (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>The dishes ordered, one line per dish.</summary>
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}