using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Internal.Validation;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests.Internal;

public class SuppressKeyReadFailuresTests : TestBase
{
    [Fact]
    public void ReturnsValue_WhenReaderSucceeds()
    {
        var result = OperationPreValidationHelper.SuppressKeyReadFailures(() => 42);

        result.ShouldBe(42);
    }

    [Fact]
    public void SwallowsInvalidOperationException_OnlyWhenRaisedByEntityFrameworkCore()
    {
        // Trigger an EF Core InvalidOperationException by asking for the entry of an
        // entity type the model doesn't know — produces the exception with TargetSite
        // in Microsoft.EntityFrameworkCore. Confirms the suppression honours its
        // narrower scope without resorting to brittle string matching.
        using var context = CreateContext();

        var result = OperationPreValidationHelper.SuppressKeyReadFailures<int>(
            () => { context.Entry(new UnknownEntity()); return 0; });

        result.ShouldBe(0);
    }

    [Fact]
    public void Propagates_InvalidOperationException_FromUserCode()
    {
        // Direct user-thrown InvalidOperationException is no longer swallowed; only
        // exceptions originating in EF Core's assembly are. Regression-locks B3.
        Should.Throw<InvalidOperationException>(() =>
            OperationPreValidationHelper.SuppressKeyReadFailures<int>(
                () => throw new InvalidOperationException("shadow key")));
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

    private sealed class UnknownEntity
    {
        public int Id { get; set; }
    }
}
