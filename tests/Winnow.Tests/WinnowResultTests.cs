using Shouldly;

namespace Winnow.Tests;

public class WinnowResultTests
{
    [Fact]
    public void EmptyResult_HasZeroSuccessAndFailure()
    {
        var result = new WinnowResult<int>();

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
    }

    [Fact]
    public void CompleteSuccess_ReturnsCorrectStatus()
    {
        var result = new WinnowResult<int>
        {
            SuccessfulIds = new List<int> { 1, 2, 3 }
        };

        result.IsCompleteSuccess.ShouldBeTrue();
        result.IsCompleteFailure.ShouldBeFalse();
        result.IsPartialSuccess.ShouldBeFalse();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public void CompleteFailure_ReturnsCorrectStatus()
    {
        var result = new WinnowResult<int>
        {
            Failures = new List<WinnowFailure<int>>
            {
                new() { EntityId = 1, ErrorMessage = "Error 1", Reason = FailureReason.ValidationError },
                new() { EntityId = 2, ErrorMessage = "Error 2", Reason = FailureReason.ValidationError }
            }
        };

        result.IsCompleteSuccess.ShouldBeFalse();
        result.IsCompleteFailure.ShouldBeTrue();
        result.IsPartialSuccess.ShouldBeFalse();
        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(2);
    }

    [Fact]
    public void PartialSuccess_ReturnsCorrectStatus()
    {
        var result = new WinnowResult<int>
        {
            SuccessfulIds = new List<int> { 1, 2 },
            Failures = new List<WinnowFailure<int>>
            {
                new() { EntityId = 3, ErrorMessage = "Error", Reason = FailureReason.ValidationError }
            }
        };

        result.IsCompleteSuccess.ShouldBeFalse();
        result.IsCompleteFailure.ShouldBeFalse();
        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.TotalProcessed.ShouldBe(3);
    }

    [Fact]
    public void FailedIds_ReturnsCorrectEntityIds()
    {
        var result = new WinnowResult<int>
        {
            Failures = new List<WinnowFailure<int>>
            {
                new() { EntityId = 5, ErrorMessage = "Error 5", Reason = FailureReason.ValidationError },
                new() { EntityId = 10, ErrorMessage = "Error 10", Reason = FailureReason.DatabaseConstraint }
            }
        };

        var failedIds = result.FailedIds;
        failedIds.Count.ShouldBe(2);
        failedIds.ShouldContain(5);
        failedIds.ShouldContain(10);
    }

    [Fact]
    public void SuccessRate_CalculatesCorrectly()
    {
        var result = new WinnowResult<int>
        {
            SuccessfulIds = new List<int> { 1, 2, 3 },
            Failures = new List<WinnowFailure<int>>
            {
                new() { EntityId = 4, ErrorMessage = "Error", Reason = FailureReason.ValidationError }
            }
        };

        result.SuccessRate.ShouldBe(0.75);
    }

    [Fact]
    public void SuccessRate_WithNoEntities_ReturnsZero()
    {
        var result = new WinnowResult<int>();

        result.SuccessRate.ShouldBe(0);
    }

    [Fact]
    public void PerformanceMetrics_StoresCorrectly()
    {
        var duration = TimeSpan.FromSeconds(5);
        var result = new WinnowResult<int>
        {
            Duration = duration,
            DatabaseRoundTrips = 42
        };

        result.Duration.ShouldBe(duration);
        result.DatabaseRoundTrips.ShouldBe(42);
    }

    [Fact]
    public void WinnowFailure_StoresAllProperties()
    {
        var exception = new InvalidOperationException("Test exception");
        var failure = new WinnowFailure<int>
        {
            EntityId = 123,
            ErrorMessage = "Validation failed",
            Reason = FailureReason.ValidationError,
            Exception = exception
        };

        failure.EntityId.ShouldBe(123);
        failure.ErrorMessage.ShouldBe("Validation failed");
        failure.Reason.ShouldBe(FailureReason.ValidationError);
        failure.Exception.ShouldBe(exception);
    }
}
