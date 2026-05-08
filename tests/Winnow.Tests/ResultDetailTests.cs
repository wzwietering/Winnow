using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Verifies that ResultDetail correctly trades reporting detail for memory:
/// counts and DB rows are correct at every level, throwing accessors fire
/// when their data is unavailable, and load-bearing trackers (orphan deletion)
/// continue to work at None.
/// </summary>
public class ResultDetailTests : TestBase
{
    [Fact]
    public void Default_ResultDetail_IsFull_OnAllOptionsClasses()
    {
        new WinnowOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new InsertOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new DeleteOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new UpsertOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new GraphOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new InsertGraphOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new DeleteGraphOptions().ResultDetail.ShouldBe(ResultDetail.Full);
        new UpsertGraphOptions().ResultDetail.ShouldBe(ResultDetail.Full);
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    [InlineData(ResultDetail.None)]
    public void Insert_AtAnyDetail_RowsArePersistedAndCountsAreAccurate(ResultDetail detail)
    {
        using var context = CreateContext();
        var products = BuildProducts(5);

        var result = new Winnower<Product, int>(context).Insert(products, new InsertOptions { ResultDetail = detail });

        result.SuccessCount.ShouldBe(5);
        result.FailureCount.ShouldBe(0);
        result.IsCompleteSuccess.ShouldBeTrue();
        result.ResultDetail.ShouldBe(detail);
        context.Products.Count().ShouldBe(5);
    }

    [Fact]
    public void Insert_AtMinimal_InsertedIdsArePopulatedAndEntitiesThrow()
    {
        using var context = CreateContext();
        var products = BuildProducts(3);

        var result = new Winnower<Product, int>(context).Insert(products, new InsertOptions { ResultDetail = ResultDetail.Minimal });

        result.InsertedIds.Count.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id > 0);
        Should.Throw<InvalidOperationException>(() => result.InsertedEntities)
            .Message.ShouldContain("InsertedEntities");
    }

    [Fact]
    public void Insert_AtNone_AllPerEntityAccessorsThrow()
    {
        using var context = CreateContext();
        var products = BuildProducts(2);

        var result = new Winnower<Product, int>(context).Insert(products, new InsertOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.InsertedIds);
        Should.Throw<InvalidOperationException>(() => result.InsertedEntities);
        Should.Throw<InvalidOperationException>(() => result.Failures);
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    public void Insert_FailureAtMinimalOrFull_FailuresAreReported_ButExceptionDroppedAtMinimal(ResultDetail detail)
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var existingId = context.Products.First().Id;

        // Force a unique-key conflict by inserting with an already-used PK.
        var conflict = new Product
        {
            Id = existingId, // pre-existing
            Name = "Duplicate",
            Price = 1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        };

        var result = new Winnower<Product, int>(context).Insert(
            [conflict],
            new InsertOptions { Strategy = BatchStrategy.OneByOne, ResultDetail = detail });

        result.FailureCount.ShouldBe(1);
        result.Failures.Count.ShouldBe(1);
        if (detail == ResultDetail.Full)
        {
            result.Failures[0].Exception.ShouldNotBeNull();
        }
        else
        {
            result.Failures[0].Exception.ShouldBeNull();
            result.Failures[0].ErrorMessage.ShouldNotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    [InlineData(ResultDetail.None)]
    public void Update_AtAnyDetail_RowsArePersistedAndCountsAreAccurate(ResultDetail detail)
    {
        using var context = CreateContext();
        SeedData(context, 4);
        var products = context.Products.ToList();
        foreach (var p in products) p.Stock += 100;

        var result = new Winnower<Product, int>(context).Update(products, new WinnowOptions { ResultDetail = detail });

        result.SuccessCount.ShouldBe(4);
        result.ResultDetail.ShouldBe(detail);

        context.ChangeTracker.Clear();
        context.Products.All(p => p.Stock >= 100).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    [InlineData(ResultDetail.None)]
    public void Delete_AtAnyDetail_RowsAreRemovedAndCountsAreAccurate(ResultDetail detail)
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.ToList();

        var result = new Winnower<Product, int>(context).Delete(products, new DeleteOptions { ResultDetail = detail });

        result.SuccessCount.ShouldBe(3);
        context.Products.Count().ShouldBe(0);
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    [InlineData(ResultDetail.None)]
    public void Upsert_AtAnyDetail_RoutesInsertsAndUpdatesAccurately(ResultDetail detail)
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var existing = context.Products.ToList();
        foreach (var p in existing) p.Stock = 999;
        var newOnes = BuildProducts(3);
        var combined = new List<Product>(existing);
        combined.AddRange(newOnes);

        var result = new Winnower<Product, int>(context).Upsert(combined, new UpsertOptions { ResultDetail = detail });

        result.SuccessCount.ShouldBe(5);
        result.InsertedCount.ShouldBe(3);
        result.UpdatedCount.ShouldBe(2);
        if (detail == ResultDetail.Full)
        {
            result.InsertedEntities.Count.ShouldBe(3);
            result.UpdatedEntities.Count.ShouldBe(2);
        }
        else if (detail == ResultDetail.Minimal)
        {
            result.InsertedIds.Count.ShouldBe(3);
            result.UpdatedIds.Count.ShouldBe(2);
            Should.Throw<InvalidOperationException>(() => result.InsertedEntities);
        }
        else
        {
            Should.Throw<InvalidOperationException>(() => result.InsertedIds);
            Should.Throw<InvalidOperationException>(() => result.UpdatedIds);
        }
    }

    [Theory]
    [InlineData(ResultDetail.Full)]
    [InlineData(ResultDetail.Minimal)]
    [InlineData(ResultDetail.None)]
    public void InsertGraph_AtAnyDetail_PersistsHierarchyAndCountsAreAccurate(ResultDetail detail)
    {
        using var context = CreateContext();
        var orders = BuildOrdersWithItems(2, itemsPerOrder: 3);

        var result = new Winnower<CustomerOrder, int>(context).InsertGraph(
            orders, new InsertGraphOptions { ResultDetail = detail });

        result.SuccessCount.ShouldBe(2);
        context.CustomerOrders.Count().ShouldBe(2);
        context.OrderItems.Count().ShouldBe(6);

        if (detail == ResultDetail.Full)
        {
            result.GraphHierarchy.ShouldNotBeNull();
            result.GraphHierarchy!.Count.ShouldBe(2);
            result.TraversalInfo.ShouldNotBeNull();
        }
        else
        {
            Should.Throw<InvalidOperationException>(() => result.GraphHierarchy)
                .Message.ShouldContain("GraphHierarchy");
            Should.Throw<InvalidOperationException>(() => result.TraversalInfo);
        }
    }

    /// <summary>
    /// Regression: orphan-deletion is a correctness-side tracker
    /// (SingleLevelOrphanTracker / LinkChangeTrackingService) that must run
    /// regardless of ResultDetail. Verify cascading delete still happens at None.
    /// </summary>
    [Fact]
    public void UpdateGraph_AtNone_OrphanDeletionStillCascades()
    {
        using var context = CreateContext();
        var order = BuildOrdersWithItems(1, itemsPerOrder: 3).First();
        new Winnower<CustomerOrder, int>(context).InsertGraph([order]);
        context.ChangeTracker.Clear();

        var loaded = context.CustomerOrders.Include(o => o.OrderItems).Single();
        var removed = loaded.OrderItems.First();
        var removedId = removed.Id;
        loaded.OrderItems.Remove(removed);

        var result = new Winnower<CustomerOrder, int>(context).UpdateGraph([loaded], new GraphOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete,
            ResultDetail = ResultDetail.None
        });

        result.SuccessCount.ShouldBe(1);
        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedId).ShouldBeNull();
    }

    [Fact]
    public void GraphHierarchy_AtFull_RetainsTreeStructure()
    {
        using var context = CreateContext();
        var orders = BuildOrdersWithItems(1, itemsPerOrder: 2);

        var result = new Winnower<CustomerOrder, int>(context).InsertGraph(orders);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Single().Children.Count.ShouldBe(2);
    }

    [Fact]
    public void Result_RawAccessors_AreUsedByMergerWithoutThrowing()
    {
        // ParallelWinnower-style merge path is internal; verify by exercising
        // the public API end-to-end: a None-detail run still produces a valid
        // result with correct counts even though all per-entity accessors throw.
        using var context = CreateContext();
        var products = BuildProducts(10);

        var result = new Winnower<Product, int>(context).Insert(
            products,
            new InsertOptions { ResultDetail = ResultDetail.None, Strategy = BatchStrategy.DivideAndConquer });

        result.SuccessCount.ShouldBe(10);
        result.IsCompleteSuccess.ShouldBeTrue();
    }

    private static List<Product> BuildProducts(int count) => Enumerable.Range(1, count)
        .Select(i => new Product
        {
            Name = $"Product {i}",
            Price = 10m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

    private static List<CustomerOrder> BuildOrdersWithItems(int orderCount, int itemsPerOrder) =>
        Enumerable.Range(1, orderCount).Select(o => new CustomerOrder
        {
            OrderNumber = $"ORD-{o:D4}",
            CustomerId = o,
            CustomerName = $"Customer {o}",
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100m * itemsPerOrder,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, itemsPerOrder).Select(i => new OrderItem
            {
                ProductId = i,
                ProductName = $"Product {i}",
                Quantity = 1,
                UnitPrice = 100m,
                Subtotal = 100m
            }).ToList()
        }).ToList();
}
