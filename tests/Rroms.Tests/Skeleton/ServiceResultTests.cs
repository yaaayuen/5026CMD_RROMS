using Rroms.Web.Common;

namespace Rroms.Tests.Skeleton;

/// <summary>
/// Unit tests for the shared <see cref="ServiceResult"/> type every module returns.
/// </summary>
public class ServiceResultTests
{
    [Fact]
    public void Failure_WithoutMessage_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ServiceResult.Failure());
    }

    [Fact]
    public void GenericSuccess_CarriesValue()
    {
        ServiceResult<int> result = ServiceResult<int>.Success(42);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Value);
    }
}