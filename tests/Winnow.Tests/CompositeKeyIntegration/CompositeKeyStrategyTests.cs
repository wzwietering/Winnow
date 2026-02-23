using Winnow;
using Winnow.Tests.Entities;
using Shouldly;

namespace Winnow.Tests.CompositeKeyIntegration;

public class CompositeKeyStrategyTests : CompositeKeyTestBase
{
    [Fact]
    public void OneByOne_CompositeKey_IsolatesFailures()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = new[]
        {
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = null, Quantity = 5, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 2, ProductId = null, Quantity = -1, UnitPrice = 10.00m }, // Invalid
            new OrderLine { OrderId = orderId, LineNumber = 3, ProductId = null, Quantity = 3, UnitPrice = 10.00m }
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines, new InsertOptions { Strategy = BatchStrategy.OneByOne });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(1);
        result.DatabaseRoundTrips.ShouldBe(3);
    }

    [Fact]
    public void DivideAndConquer_CompositeKey_EfficientOnSuccess()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 10).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = 1,
            UnitPrice = 10.00m
        }).ToList();

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines, new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.DatabaseRoundTrips.ShouldBeLessThan(10);
    }

    [Fact]
    public void StrategiesProduceSameResults_CompositeKey()
    {
        using var context1 = CreateContext();
        using var context2 = CreateContext();

        var orderId1 = CreateCustomerOrder(context1);
        var orderId2 = CreateCustomerOrder(context2);

        var createLines = (int orderId, int count) => Enumerable.Range(1, count).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i == 3 ? -1 : 1, // Index 2 will fail
            UnitPrice = 10.00m
        }).ToList();

        var oneByOneSaver = new Winnower<OrderLine, CompositeKey>(context1);
        var oneByOneResult = oneByOneSaver.Insert(
            createLines(orderId1, 5),
            new InsertOptions { Strategy = BatchStrategy.OneByOne });

        var divideAndConquerSaver = new Winnower<OrderLine, CompositeKey>(context2);
        var divideAndConquerResult = divideAndConquerSaver.Insert(
            createLines(orderId2, 5),
            new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        oneByOneResult.SuccessCount.ShouldBe(divideAndConquerResult.SuccessCount);
        oneByOneResult.FailureCount.ShouldBe(divideAndConquerResult.FailureCount);
        oneByOneResult.Failures.Select(f => f.EntityIndex)
            .ShouldBe(divideAndConquerResult.Failures.Select(f => f.EntityIndex));
    }

    [Fact]
    public void DivideAndConquer_CompositeKey_CorrectRoundTrips()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 8).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = 1,
            UnitPrice = 10.00m
        }).ToList();

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines, new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(1); // All succeed in one batch
    }
}
