using Shouldly;

namespace Winnow.Tests;

public class ValidationCollectorTests
{
    // External consumers must be able to construct a collector to unit-test
    // their own WinnowValidator<T>. Create() exposes a public factory so the
    // otherwise-internal constructor stays hidden.
    [Fact]
    public void Create_AllowsAddingErrorsFromUserCode()
    {
        var collector = ValidationCollector.Create();
        collector.IsValid.ShouldBeTrue();

        collector.Add("Foo", "bar");

        collector.Count.ShouldBe(1);
        collector.AsSpan()[0].PropertyName.ShouldBe("Foo");
        collector.AsSpan()[0].Message.ShouldBe("bar");
    }

    [Fact]
    public void Add_NoErrors_IsValidAndCountZero()
    {
        var buffer = new ValidationError[ValidationCollector.InlineCapacity];
        var collector = new ValidationCollector(buffer);

        collector.IsValid.ShouldBeTrue();
        collector.Count.ShouldBe(0);
        collector.AsSpan().Length.ShouldBe(0);
    }

    [Fact]
    public void Add_SingleError_RecordsItInBuffer()
    {
        var buffer = new ValidationError[ValidationCollector.InlineCapacity];
        var collector = new ValidationCollector(buffer);

        collector.Add("Price", "Must be positive");

        collector.IsValid.ShouldBeFalse();
        collector.Count.ShouldBe(1);
        collector.AsSpan()[0].PropertyName.ShouldBe("Price");
        collector.AsSpan()[0].Message.ShouldBe("Must be positive");
        collector.AsSpan()[0].Code.ShouldBeNull();
    }

    [Fact]
    public void Add_ExceedsInlineCapacity_GrowsAndPreservesEarlierEntries()
    {
        var buffer = new ValidationError[ValidationCollector.InlineCapacity];
        var collector = new ValidationCollector(buffer);

        // Add one more error than the inline capacity to force growth.
        for (int i = 0; i < ValidationCollector.InlineCapacity + 1; i++)
        {
            collector.Add($"P{i}", $"M{i}");
        }

        collector.Count.ShouldBe(ValidationCollector.InlineCapacity + 1);
        var span = collector.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i].PropertyName.ShouldBe($"P{i}");
            span[i].Message.ShouldBe($"M{i}");
        }

        collector.Dispose();
    }

    [Fact]
    public void Add_WithCode_PreservesAllFields()
    {
        var buffer = new ValidationError[ValidationCollector.InlineCapacity];
        var collector = new ValidationCollector(buffer);

        collector.Add("Sku", "Required", "REQ");

        var only = collector.AsSpan()[0];
        only.PropertyName.ShouldBe("Sku");
        only.Message.ShouldBe("Required");
        only.Code.ShouldBe("REQ");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var collector = ValidationCollector.Create();
        collector.Dispose();
        collector.Dispose(); // second call must not throw
    }

    [Fact]
    public void Dispose_AfterGrow_DoesNotThrowAndCanBeUsedThroughAnotherInstance()
    {
        // Forces the collector to rent a pool buffer (count > 4) and then return
        // it on Dispose. We don't have direct access to ArrayPool to verify the
        // return, but disposing the grown collector must not throw, and a new
        // collector immediately afterwards must work correctly — guards against
        // accidental sharing of the rented buffer between independent collectors.
        var grown = ValidationCollector.Create();
        for (int i = 0; i < ValidationCollector.InlineCapacity + 2; i++)
            grown.Add($"P{i}", $"M{i}");
        grown.Count.ShouldBe(ValidationCollector.InlineCapacity + 2);
        grown.Dispose();

        var fresh = ValidationCollector.Create();
        fresh.IsValid.ShouldBeTrue();
        fresh.Count.ShouldBe(0);
        fresh.Add("After", "ok");
        fresh.AsSpan()[0].PropertyName.ShouldBe("After");
    }

    [Fact]
    public void Add_NewError_AppendsAfterPrevious()
    {
        var buffer = new ValidationError[ValidationCollector.InlineCapacity];
        var collector = new ValidationCollector(buffer);

        collector.Add(new ValidationError("A", "1"));
        collector.Add(new ValidationError("B", "2"));

        collector.AsSpan()[0].PropertyName.ShouldBe("A");
        collector.AsSpan()[1].PropertyName.ShouldBe("B");
    }
}
