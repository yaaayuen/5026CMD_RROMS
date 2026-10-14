using ImageMagick;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IMenuImageStorage"/>: dish photos are checked, resized and saved under
/// wwwroot (the folder comes from <see cref="MenuImageOptions"/>).
/// </summary>
/// <remarks>
/// Safety rules: the user's file name is never used (only its extension), the file must decode as exactly
/// the format its extension claims, it is saved again without metadata (so hidden data and GPS locations
/// are dropped), and delete only ever touches the photo folder.
/// Uses Magick.NET (Apache 2.0 licence, no licence key needed).
/// </remarks>
public class MenuImageStorage : IMenuImageStorage
{
    /// <summary>Error shown when no file was chosen or the file has no content.</summary>
    public const string EmptyFileMessage = "Choose an image file to upload.";

    /// <summary>Error shown when the file extension is not on the allowed list.</summary>
    public const string InvalidTypeMessage = "That file type is not allowed. Upload a JPG, PNG or WebP image.";

    /// <summary>Error shown when the file is bigger than the size limit.</summary>
    public const string TooLargeMessage = "That image is too large. Choose a smaller file.";

    /// <summary>Error shown when the content is not a real image, whatever the file name says.</summary>
    public const string NotAnImageMessage = "That file is not a valid image.";

    private const string JpgExtension = ".jpg";
    private const string JpegExtension = ".jpeg";
    private const string PngExtension = ".png";
    private const string WebpExtension = ".webp";

    /// <summary>The smallest height a resized photo may have, so a very wide strip never becomes zero pixels high.</summary>
    private const uint MinHeightPixels = 1;

    private readonly MenuImageOptions _options;
    private readonly IWebHostEnvironment _environment;

    /// <summary>Receives the upload limits and the web server's folder information.</summary>
    /// <param name="options">The photo limits from appsettings.json (or their defaults).</param>
    /// <param name="environment">Gives the path of the wwwroot folder.</param>
    public MenuImageStorage(IOptions<MenuImageOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<string>> SaveAsync(Stream content, string originalFileName)
    {
        if (content.Length == 0)
        {
            return ServiceResult<string>.Failure(EmptyFileMessage);
        }

        if (content.Length > _options.MaxFileSizeBytes)
        {
            return ServiceResult<string>.Failure(TooLargeMessage);
        }

        // Only the extension is taken from the user's name, and it is compared in lower case.
        string extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension))
        {
            return ServiceResult<string>.Failure(InvalidTypeMessage);
        }

        MagickFormat? format = GetFormat(extension);

        if (format == null)
        {
            return ServiceResult<string>.Failure(InvalidTypeMessage);
        }

        MagickImage? loaded = TryLoadImage(content, format.Value);

        if (loaded == null)
        {
            return ServiceResult<string>.Failure(NotAnImageMessage);
        }

        using MagickImage image = loaded;

        // Phone photos are often stored sideways with a rotation note; apply it, then drop all metadata.
        image.AutoOrient();
        image.Strip();

        // Never enlarge a small photo, only shrink a wide one.
        if (image.Width > (uint)_options.MaxWidthPixels)
        {
            uint targetWidth = (uint)_options.MaxWidthPixels;
            double scale = (double)targetWidth / image.Width;
            uint targetHeight = Math.Max(MinHeightPixels, (uint)Math.Round(image.Height * scale));

            image.Resize(targetWidth, targetHeight);
        }

        string folderPath = GetFolderPath();
        Directory.CreateDirectory(folderPath);

        // A random name stops one upload overwriting another and keeps the user's text out of the file system.
        string storedName = Guid.NewGuid().ToString("N") + extension;
        string fullPath = Path.Combine(folderPath, storedName);

        // The encoder (JPEG, PNG or WebP) is chosen from the file extension.
        await image.WriteAsync(fullPath);

        return ServiceResult<string>.Success(storedName);
    }

    /// <inheritdoc />
    public void Delete(string fileName)
    {
        // GetFileName removes any folder part, so "../../secret.txt" can only ever mean "secret.txt" in the photo folder.
        string safeName = Path.GetFileName(fileName);

        if (safeName.Length == 0)
        {
            return;
        }

        string fullPath = Path.Combine(GetFolderPath(), safeName);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <inheritdoc />
    public string GetPublicUrl(string fileName)
    {
        string folder = _options.RelativeFolder.Trim('/');
        string safeName = Path.GetFileName(fileName);

        return $"/{folder}/{safeName}";
    }

    /// <summary>The photo folder on disk, inside wwwroot.</summary>
    private string GetFolderPath()
    {
        return Path.Combine(_environment.WebRootPath, _options.RelativeFolder);
    }

    /// <summary>Maps an allowed extension to the one image format it may contain, or null when it is not supported.</summary>
    /// <param name="extension">A lower-case extension with its dot, for example ".png".</param>
    private static MagickFormat? GetFormat(string extension)
    {
        switch (extension)
        {
            case JpgExtension:
            case JpegExtension:
                return MagickFormat.Jpeg;
            case PngExtension:
                return MagickFormat.Png;
            case WebpExtension:
                return MagickFormat.WebP;
            default:
                return null;
        }
    }

    /// <summary>
    /// Tries to read the bytes as the format the extension claims. Returns null when they are not that
    /// kind of image, which is a normal bad-input case for the user to fix, not an application error.
    /// </summary>
    /// <param name="content">The uploaded bytes.</param>
    /// <param name="format">The only format the file is allowed to be.</param>
    private static MagickImage? TryLoadImage(Stream content, MagickFormat format)
    {
        // Forcing one format means a renamed text file, or a file of another type, fails to decode.
        MagickReadSettings settings = new MagickReadSettings
        {
            Format = format
        };

        try
        {
            return new MagickImage(content, settings);
        }
        catch (MagickException)
        {
            return null;
        }
    }
}