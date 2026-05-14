using Shouldly;
using Winnow.Internal.Validation;

namespace Winnow.Tests.Internal;

public class SuppressKeyReadFailuresTests
{
    [Fact]
    public void ReturnsValue_WhenReaderSucceeds()
    {
        var result = OperationPreValidationHelper.SuppressKeyReadFailures(() => 42);

        result.ShouldBe(42);
    }

    [Fact]
    public void SwallowsInvalidOperationException_PreservingShadowKeyFallback()
    {
        var result = OperationPreValidationHelper.SuppressKeyReadFailures<int>(
            () => throw new InvalidOperationException("shadow key"));

        result.ShouldBe(0);
    }

    [Fact]
    public void Propagates_OutOfMemoryException()
    {
        Should.Throw<OutOfMemoryException>(() =>
            OperationPreValidationHelper.SuppressKeyReadFailures<int>(
                () => throw new OutOfMemoryException("simulated OOM")));
    }

    [Fact]
    public void Propagates_OperationCanceledException()
    {
        Should.Throw<OperationCanceledException>(() =>
            OperationPreValidationHelper.SuppressKeyReadFailures<int>(
                () => throw new OperationCanceledException()));
    }
}
