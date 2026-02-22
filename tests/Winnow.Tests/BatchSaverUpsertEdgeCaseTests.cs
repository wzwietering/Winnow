using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class BatchSaverUpsertEdgeCaseTests : TestBase
{
    [Fact]
    public void UpsertBatch_NavigationValidation_Throws()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-NAV-001",
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
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);

        Should.Throw<InvalidOperationException>(() => saver.UpsertBatch([order]))
            .Message.ShouldContain("populated navigation properties");
    }

    [Fact]
    public void UpsertBatch_NavigationValidation_Disabled()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-NAV-002",
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
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new UpsertBatchOptions { ValidateNavigationProperties = false };

        var result = saver.UpsertBatch([order], options);

        result.SuccessCount.ShouldBe(1);
    }

    [Fact]
    public void UpsertBatch_LargeBatch_Performance()
    {
        using var context = CreateContext();
        SeedData(context, 500);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 500).Select(i => new Product
        {
            Name = $"Large Batch Product {i}",
            Price = 20.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(existingProducts.Concat(newProducts));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1000);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(1000);
    }

    [Fact]
    public void UpsertBatch_DetachedEntities_Work()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        // Get products and detach
        var products = context.Products.AsNoTracking().ToList();
        foreach (var p in products)
            p.Price += 10.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        var updated = context.Products.First();
        updated.Price.ShouldBeGreaterThan(10.00m);
    }

    [Fact]
    public void UpsertBatch_TrackedEntities_Work()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var products = context.Products.ToList(); // Tracked
        foreach (var p in products)
            p.Price += 5.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(3);
    }

    [Fact]
    public void UpsertBatch_MixedTrackedDetached_Work()
    {
        using var context = CreateContext();
        SeedData(context, 4);

        var trackedProducts = context.Products.Take(2).ToList();
        foreach (var p in trackedProducts)
            p.Price += 3.00m;

        context.ChangeTracker.Clear();

        var detachedProducts = context.Products.Skip(2).Take(2).AsNoTracking().ToList();
        foreach (var p in detachedProducts)
            p.Price += 7.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(trackedProducts.Concat(detachedProducts));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(4);
    }

    [Fact]
    public void UpsertBatch_GetByIndex_FindsEntity()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        var entity = result.GetByIndex(1);
        entity.ShouldNotBeNull();
        entity.OriginalIndex.ShouldBe(1);
    }

    [Fact]
    public void UpsertBatch_GetFailureByIndex_FindsFailure()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P0", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P1", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = 15.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        var failure = result.GetFailureByIndex(1);
        failure.ShouldNotBeNull();
        failure.EntityIndex.ShouldBe(1);
    }

    [Fact]
    public void UpsertGraphBatch_SelfReferencingHierarchy_Works()
    {
        using var context = CreateContext();

        var parentCategory = new Category
        {
            Name = "Parent Category",
            Description = "Top level",
            SubCategories =
            [
                new Category
                {
                    Name = "Child Category 1",
                    Description = "First child"
                },
                new Category
                {
                    Name = "Child Category 2",
                    Description = "Second child"
                }
            ]
        };

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.UpsertGraphBatch([parentCategory]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(3);
        var parent = context.Categories.Include(c => c.SubCategories)
            .First(c => c.ParentCategoryId == null);
        parent.SubCategories.Count.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_CircularReference_Handled()
    {
        using var context = CreateContext();

        var category1 = new Category
        {
            Name = "Category 1",
            Description = "First"
        };

        context.Categories.Add(category1);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Add a child that references back
        var loadedCategory = context.Categories.First();
        loadedCategory.SubCategories.Add(new Category
        {
            Name = "Child Category",
            Description = "Child"
        });

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.UpsertGraphBatch([loadedCategory], new UpsertGraphBatchOptions
        {
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_MaxDepth_Respected()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-DEPTH",
            CustomerName = "Depth Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 300.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Deep Product",
                    Quantity = 5,
                    UnitPrice = 60.00m,
                    Subtotal = 300.00m,
                    Reservations =
                    [
                        new ItemReservation
                        {
                            WarehouseLocation = "WH-DEEP",
                            ReservedQuantity = 3,
                            ReservedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order], new UpsertGraphBatchOptions
        {
            MaxDepth = 1 // Only parent and immediate children
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(1);
        // Reservations should not be inserted due to MaxDepth = 1
        context.ItemReservations.Count().ShouldBe(0);
    }

    [Fact]
    public void UpsertGraphBatch_EmptyGraph_NoErrors()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-EMPTY",
            CustomerName = "Empty Graph Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = [] // Empty
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(0);
    }
}
