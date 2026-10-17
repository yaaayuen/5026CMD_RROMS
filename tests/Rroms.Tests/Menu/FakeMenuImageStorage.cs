using Rroms.Web.Common;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// A stand-in for the real photo storage, for tests that need a menu service but no real images.
/// It accepts everything and hands out the names photo1.png, photo2.png, ... Owner: YJ.
/// </summary>
public class FakeMenuImageStorage : IMenuImageStorage
{
    private int _savedCount;

    /// <inheritdoc />
    public Task<ServiceResult<string>> SaveAsync(Stream content, string originalFileName)
    {
        _savedCount++;

        return Task.FromResult(ServiceResult<string>.Success($"photo{_savedCount}.png"));
    }

    /// <inheritdoc />
    public void Delete(string fileName)
    {
    }

    /// <inheritdoc />
    public string GetPublicUrl(string fileName)
    {
        return "/uploads/menu/" + fileName;
    }
}
