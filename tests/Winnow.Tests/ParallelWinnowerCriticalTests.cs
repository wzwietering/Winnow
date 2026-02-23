using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerCriticalTests : ParallelTestBase
{
    // === PARALLEL EXECUTION VERIFICATION ===

    [Fact]
    public async Task InsertAsync_WithHighParallelism_AllPartitionsExecute()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 4);
        var products = Enumerable.Range(1, 20).Select(i => new Product
        {
            Name = $"Parallel Product {i}",
            Price = 10.00m + i,
            Stock = 100 + i,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var result = await saver.InsertAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(20);

        var dbProducts = QueryWithFactory(ctx => ctx.Products.ToList());
        dbProducts.Count.ShouldBe(20);
        dbProducts.Select(p => p.Id).Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public async Task UpdateAsync_MultiplePartitions_AllContextsCreatedSeparately()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 8));

        var contextIds = new List<int>();
        var innerFactory = CreateContextFactory();
        Func<DbContext> trackingFactory = () =>
        {
            var ctx = innerFactory();
            lock (contextIds) { contextIds.Add(ctx.GetHashCode()); }
            return ctx;
        };

        var saver = new ParallelWinnower<Product, int>(trackingFactory, maxDegreeOfParallelism: 4);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        // Clear the IDs accumulated during constructor validation
        contextIds.Clear();

        await saver.UpdateAsync(products);

        // Each partition should get its own context
        var uniqueContexts = contextIds.Distinct().Count();
        uniqueContexts.ShouldBeGreaterThan(1, "Multiple partitions should create multiple contexts");
    }

    // === DBCONTEXT DISPOSAL VERIFICATION ===

    [Fact]
    public async Task UpdateAsync_AllContextsDisposed_OnSuccess()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var tracker = new DisposalTracker(CreateContextFactory());
        var saver = new ParallelWinnower<Product, int>(tracker.Factory, maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        tracker.Reset();
        await saver.UpdateAsync(products);

        tracker.AllDisposed.ShouldBeTrue(
            $"Created: {tracker.CreatedCount}, Disposed: {tracker.DisposedCount}");
    }

    [Fact]
    public async Task UpdateAsync_AllContextsDisposed_OnFailure()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var tracker = new DisposalTracker(CreateContextFactory());
        var saver = new ParallelWinnower<Product, int>(tracker.Factory, maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        products[0].Price = -10;
        foreach (var p in products.Skip(1)) p.Price += 5;

        tracker.Reset();
        await saver.UpdateAsync(products);

        tracker.AllDisposed.ShouldBeTrue(
            $"Created: {tracker.CreatedCount}, Disposed: {tracker.DisposedCount}");
    }

    [Fact]
    public async Task UpdateAsync_AllContextsDisposed_OnCancellation()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var tracker = new DisposalTracker(CreateContextFactory());
        var saver = new ParallelWinnower<Product, int>(tracker.Factory, maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        tracker.Reset();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await saver.UpdateAsync(products, cts.Token);

        // Pre-cancelled token may skip context creation entirely (0 created = 0 to dispose = OK)
        // If any were created, they must all be disposed
        tracker.NoLeaks.ShouldBeTrue(
            $"Created: {tracker.CreatedCount}, Disposed: {tracker.DisposedCount}");
    }

    [Fact]
    public async Task InsertAsync_AllContextsDisposed_OnSuccess()
    {
        EnsureDatabaseCreated();

        var tracker = new DisposalTracker(CreateContextFactory());
        var saver = new ParallelWinnower<Product, int>(tracker.Factory, maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        tracker.Reset();
        await saver.InsertAsync(products);

        tracker.AllDisposed.ShouldBeTrue(
            $"Created: {tracker.CreatedCount}, Disposed: {tracker.DisposedCount}");
    }

    // === NON-ATOMICITY VERIFICATION ===

    [Fact]
    public async Task UpdateAsync_PartialFailure_SuccessfulPartitionPersistsInDb()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

        // Partition 1 (products[0], products[1]): make product[0] invalid
        products[0].Price = -10;
        products[1].Price = 99.99m;
        // Partition 2 (products[2], products[3]): all valid
        products[2].Price = 88.88m;
        products[3].Price = 77.77m;

        var result = await saver.UpdateAsync(products);

        result.IsPartialSuccess.ShouldBeTrue();

        var dbProducts = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());
        var successfulPrices = dbProducts.Where(p => p.Price == 88.88m || p.Price == 77.77m).ToList();
        successfulPrices.Count.ShouldBeGreaterThan(0, "Successful partition should persist in DB");
    }

    [Fact]
    public async Task InsertAsync_PartialFailure_SuccessfulPartitionPersistsInDb()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new List<Product>
        {
            // Partition 1: one invalid
            new() { Name = "Invalid", Price = -5, Stock = 10, LastModified = DateTimeOffset.UtcNow },
            new() { Name = "Valid1", Price = 15, Stock = 10, LastModified = DateTimeOffset.UtcNow },
            // Partition 2: all valid
            new() { Name = "Valid2", Price = 20, Stock = 10, LastModified = DateTimeOffset.UtcNow },
            new() { Name = "Valid3", Price = 25, Stock = 10, LastModified = DateTimeOffset.UtcNow }
        };

        var result = await saver.InsertAsync(products);

        result.IsPartialSuccess.ShouldBeTrue();

        var dbProducts = QueryWithFactory(ctx => ctx.Products.ToList());
        dbProducts.Count.ShouldBeGreaterThan(0, "At least one partition should have committed");
    }

    // === ALTERNATE KEY TYPE TESTS ===

    [Fact]
    public async Task InsertAsync_WithLongKeys_Works()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<ProductLong, long>(factory, maxDegreeOfParallelism: 2);
        var products = Enumerable.Range(1, 6).Select(i => new ProductLong
        {
            Name = $"Long Product {i}",
            Price = 10.00m + i,
            Stock = 100 + i,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var result = await saver.InsertAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.InsertedEntities.ShouldAllBe(e => e.Id > 0);
    }

    [Fact]
    public async Task InsertAsync_WithGuidKeys_Works()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<ProductGuid, Guid>(factory, maxDegreeOfParallelism: 2);
        var products = Enumerable.Range(1, 6).Select(i => new ProductGuid
        {
            Id = Guid.NewGuid(),
            Name = $"Guid Product {i}",
            Price = 10.00m + i,
            Stock = 100 + i,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var result = await saver.InsertAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.InsertedEntities.ShouldAllBe(e => e.Id != Guid.Empty);
    }

    [Fact]
    public async Task UpdateAsync_WithLongKeys_Works()
    {
        EnsureDatabaseCreated();
        SeedLongProducts(6);

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<ProductLong, long>(factory, maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.ProductLongs.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    // === SYNC METHOD TESTS ===

    [Fact]
    public void Insert_Sync_Works()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.InsertedEntities.Count.ShouldBe(4);
    }

    [Fact]
    public void Delete_Sync_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var result = saver.Delete(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);

        var remaining = QueryWithFactory(ctx => ctx.Products.ToList());
        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public void Upsert_Sync_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 2));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var existing = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in existing) p.Price += 10;

        var newProducts = new TestDataBuilder().CreateValidProducts(2);
        foreach (var p in newProducts) p.Id = 0;

        var all = existing.Concat(newProducts).ToList();
        var result = saver.Upsert(all);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void InsertGraph_Sync_Works()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = CreateSyncOrders(2, 1);
        var result = saver.InsertGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    [Fact]
    public void DeleteGraph_Sync_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 1));

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        var result = saver.DeleteGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraph_Sync_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 1));

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        var result = saver.UpdateGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraph_Sync_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 1));

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Completed;

        var result = saver.UpsertGraph(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    // === HELPERS ===

    private void SeedLongProducts(int count)
    {
        SeedWithFactory(ctx =>
        {
            for (int i = 1; i <= count; i++)
            {
                ctx.ProductLongs.Add(new ProductLong
                {
                    Name = $"Long Product {i}",
                    Price = 10.00m + i,
                    Stock = 100 + i,
                    LastModified = DateTimeOffset.UtcNow
                });
            }

            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();
        });
    }

    private static List<CustomerOrder> CreateSyncOrders(int count, int itemsPerOrder)
    {
        return Enumerable.Range(1, count).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-SYNC-{i:D6}",
            CustomerId = 1000 + i,
            CustomerName = $"Customer {i}",
            Status = CustomerOrderStatus.Pending,
            TotalAmount = itemsPerOrder * 22m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, itemsPerOrder).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Product {j}",
                Quantity = 2,
                UnitPrice = 11m,
                Subtotal = 22m
            }).ToList<OrderItem>()
        }).ToList();
    }

    /// <summary>
    /// Tracks DbContext creation and disposal by probing the context after the operation completes.
    /// </summary>
    private class DisposalTracker
    {
        private readonly Func<DbContext> _innerFactory;
        private readonly List<DbContext> _contexts = [];

        public DisposalTracker(Func<DbContext> innerFactory)
        {
            _innerFactory = innerFactory;
        }

        public Func<DbContext> Factory => () =>
        {
            var ctx = _innerFactory();
            lock (_contexts) { _contexts.Add(ctx); }
            return ctx;
        };

        public int CreatedCount
        {
            get { lock (_contexts) { return _contexts.Count; } }
        }

        public int DisposedCount
        {
            get
            {
                lock (_contexts)
                {
                    return _contexts.Count(IsDisposed);
                }
            }
        }

        public bool AllDisposed
        {
            get
            {
                lock (_contexts)
                {
                    return _contexts.Count > 0 && _contexts.All(IsDisposed);
                }
            }
        }

        /// <summary>
        /// True if no contexts were leaked: either none were created, or all were disposed.
        /// </summary>
        public bool NoLeaks
        {
            get
            {
                lock (_contexts)
                {
                    return _contexts.Count == 0 || _contexts.All(IsDisposed);
                }
            }
        }

        public void Reset()
        {
            lock (_contexts) { _contexts.Clear(); }
        }

        private static bool IsDisposed(DbContext context)
        {
            try
            {
                _ = context.ChangeTracker.HasChanges();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }
}
