using EfCoreUtils;
using EfCoreUtils.Tests.Entities;
using Shouldly;

namespace EfCoreUtils.Tests.CompositeKeyIntegration;

public class CompositeKeyErrorTests : CompositeKeyTestBase
{
    [Fact]
    public void NullComponent_ThrowsDescriptiveError()
    {
        Should.Throw<ArgumentNullException>(() => new CompositeKey(null!));
    }

    [Fact]
    public void ErrorMessage_FormatsReadably()
    {
        var key = new CompositeKey(1, 2, "test");
        key.ToString().ShouldBe("(1, 2, test)");
    }

    [Fact]
    public void InsertBatch_ValidationError_CorrectIndices()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = new[]
        {
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = null, Quantity = 5, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 2, ProductId = null, Quantity = -1, UnitPrice = 10.00m }, // Invalid
            new OrderLine { OrderId = orderId, LineNumber = 3, ProductId = null, Quantity = 3, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 4, ProductId = null, Quantity = -2, UnitPrice = 10.00m } // Invalid
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines);

        result.IsPartialSuccess.ShouldBeTrue();
        result.FailureCount.ShouldBe(2);

        var failedIndices = result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ToList();
        failedIndices.ShouldBe([1, 3]);
    }

    [Fact]
    public void DeleteBatch_VerifyDatabaseState()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 2);

        var linesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        linesToDelete.Count.ShouldBe(2);

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.DeleteBatch(linesToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void GetValue_OutOfRange_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => key.GetValue<int>(5));
        ex.Message.ShouldContain("out of range");
        ex.Message.ShouldContain("2 component(s)");
    }

    [Fact]
    public void Indexer_OutOfRange_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => _ = key[-1]);
        ex.Message.ShouldContain("out of range");
    }

    [Fact]
    public void EmptyArray_ThrowsDescriptiveError()
    {
        var ex = Should.Throw<ArgumentException>(() => new CompositeKey(Array.Empty<object>()));
        ex.Message.ShouldContain("at least one component");
    }

    [Fact]
    public void NullComponentInArray_ThrowsDescriptiveError()
    {
        var ex = Should.Throw<ArgumentException>(() => new CompositeKey(1, null!, 3));
        ex.Message.ShouldContain("cannot be null");
        ex.Message.ShouldContain("index 1");
    }

    [Fact]
    public void GetValue_IncompatibleType_ThrowsDescriptiveError()
    {
        var key = new CompositeKey("text", 42);

        var ex = Should.Throw<InvalidCastException>(() => key.GetValue<int>(0));
        ex.Message.ShouldContain("String");
        ex.Message.ShouldContain("Int32");
    }

    [Fact]
    public void AsSingle_MultipleComponents_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<InvalidOperationException>(() => key.AsSingle<int>());
        ex.Message.ShouldContain("2 components");
        ex.Message.ShouldContain("GetValue<T>(index)");
    }

    [Fact]
    public void Deconstruct_WrongCount_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2, 3);

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            var (_, _) = key; // Trying to deconstruct 3-part key into 2 parts
        });
        ex.Message.ShouldContain("Cannot deconstruct into 2 components");
        ex.Message.ShouldContain("3 component(s)");
    }

    [Fact]
    public void Deconstruct_FourParts_WrongCount_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            var (_, _, _, _) = key; // Trying to deconstruct 2-part key into 4 parts
        });
        ex.Message.ShouldContain("Cannot deconstruct into 4 components");
        ex.Message.ShouldContain("2 component(s)");
    }
}
