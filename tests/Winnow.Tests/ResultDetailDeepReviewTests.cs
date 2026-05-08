using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Internal.Accumulators;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Deep-review regression tests covering coverage gaps and findings from the
/// 1.1.0 review: empty-input ResultDetail propagation, accumulator unit
/// behaviour for the previously untested types, graph-operation × detail
/// matrix, and DivideAndConquer failure offset remapping at sub-Full detail.
/// </summary>
public class ResultDetailDeepReviewTests : TestBase
{
    // ---- C1 regression: empty-input path preserves ResultDetail ----

    [Fact]
    public void Insert_EmptyCollection_AtNone_ResultDetailIsPreserved()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        var result = saver.Insert([], new InsertOptions { ResultDetail = ResultDetail.None });

        result.ResultDetail.ShouldBe(ResultDetail.None);
    }

    [Fact]
    public void Update_EmptyCollection_AtMinimal_ResultDetailIsPreserved()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        var result = saver.Update([], new WinnowOptions { ResultDetail = ResultDetail.Minimal });

        result.ResultDetail.ShouldBe(ResultDetail.Minimal);
    }

    [Fact]
    public void InsertGraph_EmptyCollection_AtNone_ResultDetailIsPreserved()
    {
        using var context = CreateContext();
        var saver = new Winnower<CustomerOrder, int>(context);

        var result = saver.InsertGraph([], new InsertGraphOptions { ResultDetail = ResultDetail.None });

        result.ResultDetail.ShouldBe(ResultDetail.None);
    }

    [Fact]
    public void UpsertGraph_EmptyCollection_AtMinimal_ResultDetailIsPreserved()
    {
        using var context = CreateContext();
        var saver = new Winnower<CustomerOrder, int>(context);

        var result = saver.UpsertGraph([], new UpsertGraphOptions { ResultDetail = ResultDetail.Minimal });

        result.ResultDetail.ShouldBe(ResultDetail.Minimal);
    }

    // ---- Coverage gap: DeleteGraph at sub-Full detail ----

    [Fact]
    public void DeleteGraph_AtNone_RowsAreDeletedAndAccessorsThrow()
    {
        using var context = CreateContext();
        var orders = BuildOrdersWithItems(2, itemsPerOrder: 2);
        new Winnower<CustomerOrder, int>(context).InsertGraph(orders);
        context.ChangeTracker.Clear();

        var loaded = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var result = new Winnower<CustomerOrder, int>(context).DeleteGraph(
            loaded, new DeleteGraphOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(2);
        result.ResultDetail.ShouldBe(ResultDetail.None);
        Should.Throw<InvalidOperationException>(() => result.GraphHierarchy);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.OrderItems.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteGraph_AtMinimal_SuccessfulIdsPopulatedHierarchyThrows()
    {
        using var context = CreateContext();
        var orders = BuildOrdersWithItems(2, itemsPerOrder: 1);
        new Winnower<CustomerOrder, int>(context).InsertGraph(orders);
        context.ChangeTracker.Clear();

        var loaded = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var result = new Winnower<CustomerOrder, int>(context).DeleteGraph(
            loaded, new DeleteGraphOptions { ResultDetail = ResultDetail.Minimal });

        result.SuccessfulIds.Count.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.GraphHierarchy);
    }

    // ---- Coverage gap: UpsertGraph at sub-Full detail ----

    [Fact]
    public void UpsertGraph_AtMinimal_CountsCorrectAndEntityAccessorsThrow()
    {
        using var context = CreateContext();
        var newOrders = BuildOrdersWithItems(2, itemsPerOrder: 2);

        var result = new Winnower<CustomerOrder, int>(context).UpsertGraph(
            newOrders, new UpsertGraphOptions { ResultDetail = ResultDetail.Minimal });

        result.SuccessCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(2);
        result.InsertedIds.Count.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.InsertedEntities);
        Should.Throw<InvalidOperationException>(() => result.GraphHierarchy);
    }

    [Fact]
    public void UpsertGraph_AtNone_PersistsRowsAndCountsCorrect()
    {
        using var context = CreateContext();
        var newOrders = BuildOrdersWithItems(3, itemsPerOrder: 1);

        var result = new Winnower<CustomerOrder, int>(context).UpsertGraph(
            newOrders, new UpsertGraphOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(3);
        result.InsertedCount.ShouldBe(3);
        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(3);
    }

    // ---- Coverage gap: UpdateGraph at Minimal ----

    [Fact]
    public void UpdateGraph_AtMinimal_SuccessfulIdsPopulatedAndHierarchyThrows()
    {
        using var context = CreateContext();
        var orders = BuildOrdersWithItems(2, itemsPerOrder: 1);
        new Winnower<CustomerOrder, int>(context).InsertGraph(orders);
        context.ChangeTracker.Clear();

        var loaded = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var o in loaded) o.Status = CustomerOrderStatus.Completed;

        var result = new Winnower<CustomerOrder, int>(context).UpdateGraph(
            loaded, new GraphOptions { ResultDetail = ResultDetail.Minimal });

        result.SuccessfulIds.Count.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.GraphHierarchy);
    }

    // ---- Direct accumulator unit tests (InsertAccumulator, WinnowAccumulator) ----

    [Fact]
    public void InsertAccumulator_RecordSuccess_AtMinimal_PopulatesIdsAndEntitiesThrows()
    {
        var accumulator = new InsertAccumulator<int>(ResultDetail.Minimal);

        accumulator.RecordSuccess(id: 42, index: 0, entity: new object());
        var result = accumulator.Build(wasCancelled: false);

        result.InsertedIds.ShouldBe([42]);
        Should.Throw<InvalidOperationException>(() => result.InsertedEntities);
    }

    [Fact]
    public void InsertAccumulator_RecordSuccess_AtNone_CountsOnlyAccessorsThrow()
    {
        var accumulator = new InsertAccumulator<int>(ResultDetail.None);

        accumulator.RecordSuccess(id: 1, index: 0, entity: new object());
        accumulator.RecordSuccess(id: 2, index: 1, entity: new object());
        var result = accumulator.Build(wasCancelled: false);

        result.SuccessCount.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.InsertedIds);
    }

    [Fact]
    public void InsertAccumulator_RecordFailure_AtMinimal_ExceptionIsNull()
    {
        var accumulator = new InsertAccumulator<int>(ResultDetail.Minimal);

        accumulator.RecordFailure(
            index: 0, errorMessage: "boom",
            reason: FailureReason.UnknownError,
            exception: new InvalidOperationException("inner"));
        var result = accumulator.Build(wasCancelled: false);

        result.Failures[0].Exception.ShouldBeNull();
    }

    [Fact]
    public void InsertAccumulator_RecordFailure_AtFull_ExceptionIsRetained()
    {
        var accumulator = new InsertAccumulator<int>(ResultDetail.Full);
        var ex = new InvalidOperationException("inner");

        accumulator.RecordFailure(
            index: 0, errorMessage: "boom",
            reason: FailureReason.UnknownError, exception: ex);
        var result = accumulator.Build(wasCancelled: false);

        result.Failures[0].Exception.ShouldBeSameAs(ex);
    }

    [Fact]
    public void WinnowAccumulator_RecordFailure_AtMinimal_ExceptionIsNull()
    {
        var accumulator = new WinnowAccumulator<int>(ResultDetail.Minimal);

        accumulator.RecordFailure(
            id: 7, errorMessage: "boom",
            reason: FailureReason.UnknownError,
            exception: new InvalidOperationException("inner"));
        var result = accumulator.Build(wasCancelled: false);

        result.Failures[0].Exception.ShouldBeNull();
    }

    [Fact]
    public void WinnowAccumulator_RecordSuccess_AtNone_DoesNotPopulateSuccessfulIds()
    {
        var accumulator = new WinnowAccumulator<int>(ResultDetail.None);

        accumulator.RecordSuccess(id: 5);
        var result = accumulator.Build(wasCancelled: false);

        result.SuccessCount.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => result.SuccessfulIds);
    }

    // ---- DivideAndConquer failure offset remapping at Minimal ----

    [Fact]
    public void Insert_DivideAndConquer_AtMinimal_FailuresHaveDistinctEntityIndices()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var existingIds = context.Products.Select(p => p.Id).ToList();

        var products = new List<Product>();
        products.AddRange(BuildProducts(3));

        var dupA = new Product { Id = existingIds[0], Name = "DupA", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow };
        var dupB = new Product { Id = existingIds[1], Name = "DupB", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow };
        products.Add(dupA);
        products.Add(dupB);
        products.AddRange(BuildProducts(3));

        var result = new Winnower<Product, int>(context).Insert(
            products,
            new InsertOptions
            {
                Strategy = BatchStrategy.DivideAndConquer,
                ResultDetail = ResultDetail.Minimal
            });

        result.FailureCount.ShouldBe(2);
        result.Failures.Select(f => f.EntityIndex).Distinct().Count().ShouldBe(2);
    }

    // ---- M3 regression: GetByIndex throws below Full and is correct at Full ----

    [Fact]
    public void UpsertResult_GetByIndex_AtFull_ReturnsEntityAtRequestedIndex()
    {
        using var context = CreateContext();
        var products = BuildProducts(3);

        var result = new Winnower<Product, int>(context).Upsert(products);

        var entry = result.GetByIndex(1);
        entry.ShouldNotBeNull();
        entry.OriginalIndex.ShouldBe(1);
    }

    [Fact]
    public void UpsertResult_GetByIndex_AtMinimal_Throws()
    {
        using var context = CreateContext();
        var products = BuildProducts(2);

        var result = new Winnower<Product, int>(context).Upsert(
            products, new UpsertOptions { ResultDetail = ResultDetail.Minimal });

        Should.Throw<InvalidOperationException>(() => result.GetByIndex(0));
    }

    // ---- M1 regression: graph-property guard message no longer steers to counts ----

    [Fact]
    public void GraphHierarchy_GuardMessage_AtMinimal_DoesNotSuggestSuccessCountFallback()
    {
        using var context = CreateContext();
        var products = BuildProducts(1);

        var result = new Winnower<Product, int>(context).Insert(
            products, new InsertOptions { ResultDetail = ResultDetail.Minimal });

        var ex = Should.Throw<InvalidOperationException>(() => result.GraphHierarchy);
        ex.Message.ShouldNotContain("SuccessCount");
    }

    private static List<Product> BuildProducts(int count) => Enumerable.Range(1, count)
        .Select(i => new Product
        {
            Name = $"Product {Guid.NewGuid():N}",
            Price = 10m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

    private static List<CustomerOrder> BuildOrdersWithItems(int orderCount, int itemsPerOrder) =>
        Enumerable.Range(1, orderCount).Select(o => new CustomerOrder
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}",
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
