using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerInsertGraphTests : TestBase
{
    [Fact]
    public void InsertGraph_ParentAndChildren_AllInsertedWithIds()
    {
        using var context = CreateContext();

        var order = CreateValidOrder("ORD-001", 3);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.InsertedEntities[0].Id.ShouldBeGreaterThan(0);

        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
    }

    [Fact]
    public void InsertGraph_ReturnsGraphHierarchy()
    {
        using var context = CreateContext();

        var order = CreateValidOrder("ORD-001", 3);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.ShouldContain(n => n.EntityId.Equals(order.Id));
        result.GraphHierarchy!.First(n => n.EntityId.Equals(order.Id)).GetChildIds().Count.ShouldBe(3);
    }

    [Fact]
    public void InsertGraph_MultipleGraphs_EachIsolated()
    {
        using var context = CreateContext();

        var orders = new[]
        {
            CreateValidOrder("ORD-001", 2),
            CreateValidOrder("ORD-002", 3),
            CreateValidOrder("ORD-003", 1)
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.GraphHierarchy!.Count.ShouldBe(3);
    }

    [Fact]
    public void InsertGraph_ParentFails_EntireGraphFails()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = -100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = [CreateValidOrderItem(1)]
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_ChildFails_EntireGraphFails()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Test Product",
                    Quantity = -5,
                    UnitPrice = 50.00m,
                    Subtotal = -250.00m
                }
            ]
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_OneGraphFails_OthersSucceed()
    {
        using var context = CreateContext();

        var orders = new[]
        {
            CreateValidOrder("ORD-001", 2),
            new CustomerOrder
            {
                OrderNumber = "ORD-002",
                CustomerName = "Test",
                CustomerId = 2,
                Status = CustomerOrderStatus.Pending,
                TotalAmount = -100.00m,
                OrderDate = DateTimeOffset.UtcNow,
                OrderItems = [CreateValidOrderItem(1)]
            },
            CreateValidOrder("ORD-003", 1)
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_EmptyChildren_ParentStillInserts()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = []
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.InsertedEntities[0].Id.ShouldBeGreaterThan(0);
        result.GraphHierarchy!.First(n => n.EntityId.Equals(order.Id)).GetChildIds().ShouldBeEmpty();
    }

    [Fact]
    public void InsertGraph_LargeBatch_Performance()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 20).Select(i =>
            CreateValidOrder($"ORD-{i:D3}", 3)).ToList();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(20);
        result.GraphHierarchy!.Count.ShouldBe(20);
    }

    [Fact]
    public void InsertGraph_DivideAndConquer_IsolatesFailures()
    {
        using var context = CreateContext();

        var orders = new[]
        {
            CreateValidOrder("ORD-001", 2),
            CreateValidOrder("ORD-002", 2),
            new CustomerOrder
            {
                OrderNumber = "ORD-003",
                CustomerName = "Test",
                CustomerId = 3,
                Status = CustomerOrderStatus.Pending,
                TotalAmount = -100.00m,
                OrderDate = DateTimeOffset.UtcNow,
                OrderItems = [CreateValidOrderItem(1)]
            },
            CreateValidOrder("ORD-004", 2)
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders, new InsertGraphOptions
        {
            Strategy = BatchStrategy.DivideAndConquer
        });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(2);
    }

    [Fact]
    public void InsertGraph_ChildIdsCorrectlyMapped()
    {
        using var context = CreateContext();

        var order = CreateValidOrder("ORD-001", 3);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        var parentId = result.InsertedEntities[0].Id;
        var childIds = result.GraphHierarchy!.First(n => n.EntityId.Equals(parentId)).GetChildIds().ToList();

        childIds.Count.ShouldBe(3);

        var actualChildIds = order.OrderItems.Select(i => i.Id).OrderBy(x => x).ToList();
        childIds.OrderBy(x => x).ShouldBe(actualChildIds);
    }

    private static CustomerOrder CreateValidOrder(string orderNumber, int itemCount)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => CreateValidOrderItem(i))
            .ToList();

        return new CustomerOrder
        {
            OrderNumber = orderNumber,
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = items.Sum(i => i.Subtotal),
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = items
        };
    }

    private static OrderItem CreateValidOrderItem(int index)
    {
        var quantity = index + 1;
        var unitPrice = 10.00m + index;
        return new OrderItem
        {
            ProductId = 1000 + index,
            ProductName = $"Product {index}",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Subtotal = quantity * unitPrice
        };
    }
}
