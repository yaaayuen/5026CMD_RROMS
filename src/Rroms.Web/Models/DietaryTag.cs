using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models;

/// <summary>
/// Something a dish can contain, such as "Dairy" or "Spicy". Owner: YY (YY-10).
/// </summary>
/// <remarks>
/// <para>
/// Shared lookup table: customers pick the tags they want to AVOID (S1, profile) and menu items
/// list the tags they CONTAIN (S3, YJ). A rule-based recommender can then compare the two sets.
/// </para>
/// <para>
/// The rows are seeded with fixed IDs (see <see cref="Seed"/>), so every database has the same tags.
/// Administrators can add more tags from Admin → Dietary tags (YY-8b); these three seeded rows stay fixed.
/// </para>
/// </remarks>
public class DietaryTag
{
    //Longest tag name allowed
    public const int NameMaxLength = 30;

    /// <summary>
    /// Fixed seed data: a proof of concept with three tags. Never change an existing ID: other tables refer to it.
    /// Administrators add more tags (e.g. nuts, seafood) at runtime through DietaryTagService (YY-8b).
    /// </summary>
    public static readonly DietaryTag[] Seed =
    {
        new DietaryTag { Id = 1, Name = "Animal products", DisplayOrder = 1 },
        new DietaryTag { Id = 2, Name = "Dairy", DisplayOrder = 2 },
        new DietaryTag { Id = 3, Name = "Spicy", DisplayOrder = 3 }
    };

    //Primary key (fixed by the seed data)
    public int Id { get; set; }

    //Name shown on checkboxes and menu badges, e.g. "Nuts"
    [Required]
    [StringLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    //Order in which tags are listed on screen
    public int DisplayOrder { get; set; }
}
