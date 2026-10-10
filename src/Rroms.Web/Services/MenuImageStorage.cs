using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IMenuImageStorage"/>: dish photos are checked, resized and saved under
/// wwwroot (the folder comes from <see cref="MenuImageOptions"/>).
/// </summary>
/// <remarks>
/// Safety rules: the user's file name is never used (only its extension), 
/// the file is decoded and saved again
/// so a renamed non-image or an image with hidden data cannot be stored as it came, 
/// and delete only ever touches the photo folder.
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

    /// <summary>ImageSharp scales proportionally when one of the two sizes is zero.</summary>
    private const int KeepAspectRatio = 0;

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

        Image? loaded = await TryLoadImageAsync(content);

        if (loaded == null)
        {
            return ServiceResult<string>.Failure(NotAnImageMessage);
        }

        using Image image = loaded;

        // Never enlarge a small photo, only shrink a wide one.
        if (image.Width > _options.MaxWidthPixels)
        {
            int targetWidth = _options.MaxWidthPixels;
            image.Mutate(operation => operation.Resize(targetWidth, KeepAspectRatio));
        }

        string folderPath = GetFolderPath();
        Directory.CreateDirectory(folderPath);

        // A random name stops one upload overwriting another
        // and keeps the user's text out of the file system.
        string storedName = Guid.NewGuid().ToString("N") + extension;
        string fullPath = Path.Combine(folderPath, storedName);

        // ImageSharp picks the encoder (JPEG, PNG or WebP) from the file extension.
        await image.SaveAsync(fullPath);

        return ServiceResult<string>.Success(storedName);
    }

    /// <inheritdoc />
    public void Delete(string fileName)
    {
        // GetFileName removes any folder part,
        // so "../../secret.txt" can only ever mean "secret.txt" in the photo folder.
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

    /// <summary>
    /// Tries to read the bytes as an image. 
    /// Returns null when they are not one, which is a normal
    /// bad-input case for the user to fix, not an application error.
    /// </summary>
    private static async Task<Image?> TryLoadImageAsync(Stream content)
    {
        try
        {
            return await Image.LoadAsync(content);
        }
        catch (Exception exception) when (IsBadImageException(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// True for the errors ImageSharp throws when the bytes are not a readable image: an unknown format
    /// (for example plain text), damaged content, or a format with no decoder.
    /// </summary>
    /// <param name="exception">The error thrown while reading the upload.</param>
    private static bool IsBadImageException(Exception exception)
    {
        return exception is UnknownImageFormatException
            || exception is InvalidImageContentException
            || exception is ImageProcessingException
            || exception is NotSupportedException;
    }
}