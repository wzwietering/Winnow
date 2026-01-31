using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverUpsertGraphTests : TestBase
{
    [Fact]
    public void UpsertGraphBatch_NewParentNewChildren_AllInserted()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 3).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-NEW-{i:D3}",
            CustomerName = $"New Customer {i}",
            CustomerId = 1000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 2).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Product {j}",
                Quantity = j + 1,
                UnitPrice = 25.00m,
                Subtotal = (j + 1) * 25.00m
            }).ToList()
        }).ToList();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(3);
        result.UpdatedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(3);
        context.OrderItems.Count().ShouldBe(6);
    }

    [Fact]
    public void UpsertGraphBatch_ExistingParentNewChildren_Mixed()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        orders[0].Status = CustomerOrderStatus.Processing;
        orders[0].OrderItems.Add(new OrderItem
        {
            ProductId = 9001,
            ProductName = "New Child Product",
            Quantity = 3,
            UnitPrice = 15.00m,
            Subtotal = 45.00m
        });

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        var updatedOrder = context.CustomerOrders.Include(o => o.OrderItems)
            .First(o => o.Id == orders[0].Id);
        updatedOrder.OrderItems.Count.ShouldBe(3);
    }

    [Fact]
    public void UpsertGraphBatch_NewParentExistingChildren_Mixed()
    {
        using var context = CreateContext();

        // First create some order items that we'll reference
        var existingOrder = new CustomerOrder
        {
            OrderNumber = "ORD-EXISTING",
            CustomerName = "Existing Customer",
            CustomerId = 500,
            Status = CustomerOrderStatus.Completed,
            TotalAmount = 50.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 5001,
                    ProductName = "Existing Product",
                    Quantity = 2,
                    UnitPrice = 25.00m,
                    Subtotal = 50.00m
                }
            ]
        };
        context.CustomerOrders.Add(existingOrder);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Create a new parent order
        var newOrder = new CustomerOrder
        {
            OrderNumber = "ORD-NEWPARENT",
            CustomerName = "New Parent Customer",
            CustomerId = 600,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 75.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 6001,
                    ProductName = "Fresh Product",
                    Quantity = 3,
                    UnitPrice = 25.00m,
                    Subtotal = 75.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([newOrder]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_ExistingParentExistingChildren_AllUpdated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Completed;
            foreach (var item in order.OrderItems)
            {
                item.Quantity += 1;
                item.Subtotal = item.Quantity * item.UnitPrice;
            }
        }

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(3);
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void UpsertGraphBatch_MixedAtAllLevels()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 2);

        var existingOrders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        existingOrders[0].Status = CustomerOrderStatus.Processing;

        var newOrder = new CustomerOrder
        {
            OrderNumber = "ORD-MIXLEVEL",
            CustomerName = "Mixed Level Customer",
            CustomerId = 9999,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 7001,
                    ProductName = "Mixed Product",
                    Quantity = 4,
                    UnitPrice = 50.00m,
                    Subtotal = 200.00m
                }
            ]
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(existingOrders.Append(newOrder));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_ThreeLevel_Works()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-3LEVEL",
            CustomerName = "Three Level Customer",
            CustomerId = 1234,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 300.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 8001,
                    ProductName = "3-Level Product",
                    Quantity = 5,
                    UnitPrice = 60.00m,
                    Subtotal = 300.00m,
                    Reservations =
                    [
                        new ItemReservation
                        {
                            WarehouseLocation = "WH-A1",
                            ReservedQuantity = 3,
                            ReservedAt = DateTimeOffset.UtcNow
                        },
                        new ItemReservation
                        {
                            WarehouseLocation = "WH-B2",
                            ReservedQuantity = 2,
                            ReservedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(1);
        context.ItemReservations.Count().ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_OrphanThrow_ThrowsOnRemoval()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpsertGraphBatch(orders));

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedItemId.ToString());
    }

    [Fact]
    public void UpsertGraphBatch_OrphanDelete_DeletesOrphans()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems)
            .First(o => o.Id == orders[0].Id);
        verifyOrder.OrderItems.Count.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_OrphanDetach_DetachesOrphans()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var orphanedItem = context.OrderItems.Find(removedItemId);
        orphanedItem.ShouldNotBeNull();
    }

    [Fact]
    public void UpsertGraphBatch_GraphHierarchy_Populated()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 2).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-HIER-{i:D3}",
            CustomerName = $"Hierarchy Customer {i}",
            CustomerId = 2000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 150.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 3).Select(j => new OrderItem
            {
                ProductId = 3000 + j,
                ProductName = $"Hierarchy Product {j}",
                Quantity = j,
                UnitPrice = 50.00m,
                Subtotal = j * 50.00m
            }).ToList()
        }).ToList();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(2);

        foreach (var order in orders)
        {
            result.GraphHierarchy!.ContainsKey(order.Id).ShouldBeTrue();
            var node = result.GraphHierarchy![order.Id];
            node.GetChildIds().Count.ShouldBe(3);
        }
    }

    [Fact]
    public void UpsertGraphBatch_TraversalInfo_Accurate()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-TRAVERSE",
            CustomerName = "Traversal Customer",
            CustomerId = 4000,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 4).Select(j => new OrderItem
            {
                ProductId = 4000 + j,
                ProductName = $"Traverse Product {j}",
                Quantity = j,
                UnitPrice = 50.00m,
                Subtotal = j * 50.00m
            }).ToList()
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.TotalEntitiesTraversed.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpsertGraphBatch_WithReferences_Works()
    {
        using var context = CreateContext();

        // Create a category first
        var category = new Category
        {
            Name = "Electronics",
            Description = "Electronic products"
        };
        context.Categories.Add(category);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Create a product with the category reference (FK only, no navigation)
        var product = new Product
        {
            Name = "Reference Product",
            Price = 100.00m,
            Stock = 50,
            LastModified = DateTimeOffset.UtcNow,
            CategoryId = category.Id
            // Category navigation is NOT populated - just the FK
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedProduct = context.Products.Include(p => p.Category).First();
        savedProduct.CategoryId.ShouldBe(category.Id);
        savedProduct.Category.ShouldNotBeNull();
    }

    [Fact]
    public void UpsertGraphBatch_ChildFailure_FailsGraph()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = -1; // Invalid child

        orders[1].Status = CustomerOrderStatus.Processing;
        orders[2].Status = CustomerOrderStatus.Cancelled;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
    }
}
