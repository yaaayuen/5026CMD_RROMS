namespace Rroms.Web.Options;

/// <summary>
/// Limits for dish photo uploads. 
/// Bound from the "MenuImages" section of appsettings.json
/// so the numbers are changed in one place, not scattered through the code.
/// </summary>

public class MenuImageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MenuImages";

    /// <summary>Largest accepted upload in bytes (default 2 MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>File extensions we accept, lower caase with the dot.</summary>
    public string[] AllowedExtensions { get; set; } = new[] { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>Images wider than this are scaled down to this width.</summary>
    public int MaxWidthPixels { get; set; } = 800;

    /// <summary>Folder under wwwroot where resized images are saved.</summary>
    public string RelativeFolder { get; set; } = "uploads/menu";

}
