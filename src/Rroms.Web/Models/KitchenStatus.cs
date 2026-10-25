namespace Rroms.Web.Models;

/// <summary>The single row that says whether the kitchen is paused (Alex). While paused, no new cooking starts.</summary>
public class KitchenStatus
{
    /// <summary>Always <see cref="SingletonId"/>; there is only one kitchen.</summary>
    public int Id { get; set; }
    public const int SingletonId = 1;
    public bool IsPaused { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
