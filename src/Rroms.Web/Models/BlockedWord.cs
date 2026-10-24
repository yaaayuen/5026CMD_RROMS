using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Models;

/// <summary>A word an administrator added to the feedback filter, on top of the built-in list (Alex).</summary>
public class BlockedWord
{
    public const int MaxLength = 40;
    public int Id { get; set; }
    /// <summary>Stored lower-case. Unique, so the same word cannot be added twice.</summary>
    [Required, StringLength(MaxLength)]
    public string Word { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
