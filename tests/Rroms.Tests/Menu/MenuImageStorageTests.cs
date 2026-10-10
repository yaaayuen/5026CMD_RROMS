using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

using Rroms.Web.Common;
using Rroms.Web.Options;
using Rroms.Web.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Rroms.Tests.Menu;

/// <summary>
/// Unit tests for the YJ-1 photo rules in <see cref="MenuImageStorage"/>.
/// </summary>
/// <remarks>
/// Every test follows Arrange (prepare data) - Act (call the method) 
/// - Assert (check the result).
/// Each test class instance saves into its own temporary folder, which is deleted afterwards,
/// so tests never leave files behind and never touch the real wwwroot.
/// </remarks>
public class MenuImageStorageTests : IDisposable
{
    private const int WideImageWidth = 1600;
    private const int SmallImageWidth = 400;
    private const int ImageHeight = 200;

    /// <summary>Smaller than any real PNG file, so every valid image is "too large" for this limit.</summary>
    private const long TinySizeLimitBytes = 10;

    private readonly string _webRoot;
    private readonly MenuImageOptions _options;
    private readonly MenuImageStorage _storage;

    /// <summary>Runs before EVERY test: creates an empty temporary web root.</summary>
    public MenuImageStorageTests()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "rroms-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);

        // The default limits: 2 MB, 800 pixels wide, folder uploads/menu.
        _options = new MenuImageOptions();
        _storage = CreateStorage(_options);
    }

    /// <summary>Runs after EVERY test: removes the temporary folder and everything saved in it.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private MenuImageStorage CreateStorage(MenuImageOptions options)
    {
        var environment = new TestWebHostEnvironment { WebRootPath = _webRoot };

        return new MenuImageStorage(Microsoft.Extensions.Options.Options.Create(options), environment);
    }

    /// <summary>Builds a real, blank PNG of the given size in memory.</summary>
    private static MemoryStream CreatePngStream(int width, int height)
    {
        using Image<Rgba32> image = new Image<Rgba32>(width, height);
        MemoryStream stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;

        return stream;
    }

    private string PhotoPath(string storedName)
    {
        return Path.Combine(_webRoot, _options.RelativeFolder, storedName);
    }

    //================================================== Save ==================================================//

    [Fact]
    public async Task Save_ValidPng_SavesUnderAGeneratedName()
    {
        // Arrange
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "holiday-photo.png");

        // Assert: saved, with a random name that keeps the extension but not the user's text.
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.EndsWith(".png", result.Value);
        Assert.DoesNotContain("holiday", result.Value);
        Assert.True(File.Exists(PhotoPath(result.Value)));
    }

    [Fact]
    public async Task Save_ImageWiderThanTheLimit_IsScaledDown()
    {
        // Arrange: 1600 pixels wide, the limit is 800.
        using MemoryStream upload = CreatePngStream(WideImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "wide.png");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        using Image saved = await Image.LoadAsync(PhotoPath(result.Value));
        Assert.Equal(_options.MaxWidthPixels, saved.Width);
    }

    [Fact]
    public async Task Save_SmallImage_IsNotEnlarged()
    {
        // Arrange: 400 pixels wide is already under the 800 limit.
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "small.png");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        using Image saved = await Image.LoadAsync(PhotoPath(result.Value));
        Assert.Equal(SmallImageWidth, saved.Width);
    }

    [Theory]
    [InlineData("photo.gif")]
    [InlineData("notes.txt")]
    [InlineData("run.exe")]
    [InlineData("noextension")]
    public async Task Save_DisallowedFileType_IsRejected(string fileName)
    {
        // Arrange: the bytes really are a PNG, but the name says otherwise, so the type check must refuse it.
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, fileName);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuImageStorage.InvalidTypeMessage, result.Errors);
    }

    [Fact]
    public async Task Save_TextFileRenamedToJpg_IsRejected()
    {
        // Arrange: an allowed extension is not enough, the content must decode as an image.
        byte[] notAnImage = Encoding.UTF8.GetBytes("This is just text, not a picture.");
        using MemoryStream upload = new MemoryStream(notAnImage);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "fake.jpg");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuImageStorage.NotAnImageMessage, result.Errors);
    }

    [Fact]
    public async Task Save_FileLargerThanTheLimit_IsRejected()
    {
        // Arrange: a valid image, but the limit is set below the size of any PNG.
        _options.MaxFileSizeBytes = TinySizeLimitBytes;
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "big.png");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuImageStorage.TooLargeMessage, result.Errors);
    }

    [Fact]
    public async Task Save_EmptyFile_IsRejected()
    {
        // Arrange
        using MemoryStream upload = new MemoryStream();

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "empty.png");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuImageStorage.EmptyFileMessage, result.Errors);
    }

    [Fact]
    public async Task Save_FileNameWithFolderTricks_StaysInsideThePhotoFolder()
    {
        // Arrange: a name that tries to climb out of the photo folder.
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);

        // Act
        ServiceResult<string> result = await _storage.SaveAsync(upload, "../../evil.png");

        // Assert: the stored name is a plain GUID plus extension, and the file is in the photo folder.
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.DoesNotContain("..", result.Value);
        Assert.DoesNotContain("/", result.Value);
        Assert.DoesNotContain("\\", result.Value);
        Assert.True(Guid.TryParseExact(Path.GetFileNameWithoutExtension(result.Value), "N", out _));
        Assert.True(File.Exists(PhotoPath(result.Value)));
    }

    //================================================== Delete and address ==================================================//

    [Fact]
    public async Task Delete_RemovesTheStoredPhoto()
    {
        // Arrange
        using MemoryStream upload = CreatePngStream(SmallImageWidth, ImageHeight);
        ServiceResult<string> saved = await _storage.SaveAsync(upload, "photo.png");
        Assert.NotNull(saved.Value);

        // Act
        _storage.Delete(saved.Value);

        // Assert
        Assert.False(File.Exists(PhotoPath(saved.Value)));
    }

    [Fact]
    public void Delete_FileThatDoesNotExist_DoesNothing()
    {
        // Act + Assert: no exception.
        _storage.Delete("missing.png");
    }

    [Fact]
    public void Delete_NameWithFolderTricks_DoesNotTouchFilesOutsideThePhotoFolder()
    {
        // Arrange: a file that sits two folders above the photo folder.
        string outsideFile = Path.Combine(_webRoot, "secret.txt");
        File.WriteAllText(outsideFile, "keep me");

        // Act
        _storage.Delete("../../secret.txt");

        // Assert
        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public void GetPublicUrl_ReturnsTheWebAddressInsideTheUploadFolder()
    {
        // Act
        string url = _storage.GetPublicUrl("abc.png");

        // Assert
        Assert.Equal("/uploads/menu/abc.png", url);
    }

    /// <summary>
    /// The smallest possible stand-in for the web server's folder information,
    /// so the storage class can be tested without starting the whole app.
    /// </summary>
    private class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ApplicationName { get; set; } = "Rroms.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Testing";
    }
}