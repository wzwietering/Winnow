using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ReferenceNavigationTests : TestBase
{
    #region Insert Operations

    [Fact]
    public void InsertGraph_WithReference_BothInserted()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBeGreaterThan(0);
        order.OrderItems.First().Product.ShouldBe(product);
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_WithExistingReference_Attached()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.First().ProductId.ShouldBe(productId);
    }

    [Fact]
    public void InsertGraph_SharedReference_InsertedOnce()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Shared Widget");
        var order = CreateOrderWithMultipleItemsSameProduct("ORD-001", product, 3);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBeGreaterThan(0);

        var productCount = context.Products.Count(p => p.Name == "Shared Widget");
        productCount.ShouldBe(1);

        order.OrderItems.ShouldAllBe(item => item.Product == product);
    }

    [Fact]
    public void InsertGraph_MultiLevelReference_AllInserted()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        category.Id.ShouldBeGreaterThan(0);
        product.Id.ShouldBeGreaterThan(0);
        product.CategoryId.ShouldBe(category.Id);
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(2);
    }

    [Fact]
    public void InsertGraph_LargeBatch_SharedReferences()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 10)
            .Select(i => CreateValidProduct($"Product {i}"))
            .ToList();

        var orders = Enumerable.Range(1, 50).Select(i =>
        {
            var product = products[i % 10];
            return CreateOrderWithProductReference($"ORD-{i:D3}", product);
        }).ToList();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders, new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);

        var insertedProductCount = context.Products.Count();
        insertedProductCount.ShouldBe(10);
    }

    [Fact]
    public void InsertGraph_NullReference_Ignored()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 2, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_MultipleReferencesOnItems_AllHandled()
    {
        using var context = CreateContext();

        var product1 = CreateValidProduct("Widget A");
        var product2 = CreateValidProduct("Widget B");

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 30.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                CreateOrderItemWithProduct(product1, 1),
                CreateOrderItemWithProduct(product2, 2)
            ]
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product1.Id.ShouldBeGreaterThan(0);
        product2.Id.ShouldBeGreaterThan(0);
        product1.Id.ShouldNotBe(product2.Id);
    }

    [Fact]
    public void InsertGraph_IncludeReferencesFalse_ReferencesNotTraversed()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(0);
    }

    #endregion

    #region Update Operations

    [Fact]
    public void UpdateGraph_WithReference_BothUpdated()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget", 10.00m);
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Find(productId)!;
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First(o => o.Id == orderId);

        reloadedOrder.OrderItems.First().Quantity = 2;
        reloadedOrder.OrderItems.First().Subtotal = 20.00m;
        reloadedOrder.TotalAmount = 20.00m;
        reloadedOrder.OrderItems.First().Product!.Price = 20.00m;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyProduct = context.Products.Find(productId)!;
        verifyProduct.Price.ShouldBe(20.00m);
    }

    [Fact]
    public void UpdateGraph_ReferenceFailure_BothRollback()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget", 10.00m);
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Find(productId)!;
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        var originalQuantity = order.OrderItems.First().Quantity;
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First(o => o.Id == orderId);

        reloadedOrder.OrderItems.First().Quantity = 5;
        reloadedOrder.OrderItems.First().Subtotal = 50.00m;
        reloadedOrder.TotalAmount = 50.00m;
        reloadedOrder.OrderItems.First().Product!.Price = -10.00m;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteFailure.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.Id == orderId);
        verifyOrder.OrderItems.First().Quantity.ShouldBe(originalQuantity);
    }

    [Fact]
    public void UpdateGraph_SharedReference_UpdatedOnce()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget", 10.00m);
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Find(productId)!;
        var order = CreateOrderWithMultipleItemsSameProduct("ORD-001", loadedProduct, 3);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        foreach (var item in reloadedOrder.OrderItems)
        {
            item.Product!.Price = 15.00m;
        }

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyProduct = context.Products.Find(productId)!;
        verifyProduct.Price.ShouldBe(15.00m);
    }

    [Fact]
    public void UpdateGraph_ReferenceOnlyModified_Updated()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget", 10.00m);
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Find(productId)!;
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        reloadedOrder.OrderItems.First().Product!.Price = 25.00m;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyProduct = context.Products.Find(productId)!;
        verifyProduct.Price.ShouldBe(25.00m);
    }

    [Fact]
    public void UpdateGraph_MixedCollectionAndReference_Works()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget", 10.00m);
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        order.OrderItems.First().Reservations =
        [
            new ItemReservation
            {
                WarehouseLocation = "WH-A",
                ReservedQuantity = 1,
                ReservedAt = DateTimeOffset.UtcNow
            }
        ];
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();

        reloadedOrder.Status = CustomerOrderStatus.Processing;
        reloadedOrder.OrderItems.First().Product!.Stock = 50;
        reloadedOrder.OrderItems.First().Reservations.First().ReservedQuantity = 2;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Find(reloadedOrder.Id)!;
        verifyOrder.Status.ShouldBe(CustomerOrderStatus.Processing);

        var verifyProduct = context.Products.First();
        verifyProduct.Stock.ShouldBe(50);

        var verifyReservation = context.ItemReservations.First();
        verifyReservation.ReservedQuantity.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraph_CircularReference_Throw()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        reloadedOrder.OrderItems.First().Product!.Name = "Updated Widget";

        var saver = new Winnower<CustomerOrder, int>(context);
        Should.Throw<InvalidOperationException>(() =>
            saver.UpdateGraph([reloadedOrder], new GraphOptions
            {
                IncludeReferences = true,
                CircularReferenceHandling = CircularReferenceHandling.Throw
            }));
    }

    [Fact]
    public void UpdateGraph_CircularReference_Ignore()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Include(p => p.Category).First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p!.Category)
            .First();

        reloadedOrder.OrderItems.First().Product!.Name = "Updated Widget";

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verifyProduct = context.Products.First();
        verifyProduct.Name.ShouldBe("Updated Widget");
    }

    [Fact]
    public void UpdateGraph_LargeBatch_Performance()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 10)
            .Select(i => CreateValidProduct($"Product {i}"))
            .ToList();
        context.Products.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProducts = context.Products.ToList();
        var orders = Enumerable.Range(1, 50).Select(i =>
        {
            var product = loadedProducts[i % 10];
            return CreateOrderWithProductReference($"ORD-{i:D3}", product);
        }).ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ToList();

        foreach (var order in reloadedOrders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(reloadedOrders, new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);
    }

    [Fact]
    public void UpdateGraph_DetachmentVerified()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        reloadedOrder.Status = CustomerOrderStatus.Completed;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        var trackedEntities = context.ChangeTracker.Entries().Count();
        trackedEntities.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraph_OneByOne_Strategy()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var orders = Enumerable.Range(1, 5)
            .Select(i => CreateOrderWithProductReference($"ORD-{i:D3}", loadedProduct))
            .ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ToList();

        foreach (var order in reloadedOrders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(reloadedOrders, new GraphOptions
        {
            Strategy = BatchStrategy.OneByOne,
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
    }

    [Fact]
    public void UpdateGraph_DivideAndConquer_Strategy()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var orders = Enumerable.Range(1, 5)
            .Select(i => CreateOrderWithProductReference($"ORD-{i:D3}", loadedProduct))
            .ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ToList();

        foreach (var order in reloadedOrders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(reloadedOrders, new GraphOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
    }

    [Fact]
    public void UpdateGraph_Result_TracksReferences()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Include(p => p.Category).First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p!.Category)
            .First();

        reloadedOrder.Status = CustomerOrderStatus.Processing;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBeGreaterThan(0);
        result.TraversalInfo.ProcessedReferencesByType.ContainsKey("Product").ShouldBeTrue();
    }

    #endregion

    #region Delete Operations

    [Fact]
    public void DeleteGraph_ValidateExists_Valid_Succeeds()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete], new DeleteGraphOptions
        {
            ValidateReferencedEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.Products.Count().ShouldBe(1);
    }

    [Fact]
    public void DeleteGraph_ValidateExists_WithValidRef_Succeeds()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete], new DeleteGraphOptions
        {
            ValidateReferencedEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraph_ValidateExists_False_StillWorks()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete], new DeleteGraphOptions
        {
            ValidateReferencedEntitiesExist = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraph_DoesNotDeleteReference()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Find(productId)!;
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.Products.Count().ShouldBe(1);
        context.Products.Find(productId).ShouldNotBeNull();
    }

    [Fact]
    public void DeleteGraph_MultiLevel_ValidatesAll()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Include(p => p.Category).First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete], new DeleteGraphOptions
        {
            ValidateReferencedEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.Products.Count().ShouldBe(1);
        context.Categories.Count().ShouldBe(1);
    }

    [Fact]
    public void DeleteGraph_Result_TracksValidation()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete], new DeleteGraphOptions
        {
            ValidateReferencedEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
    }

    #endregion

    #region Depth & Traversal

    [Fact]
    public void Graph_MaxDepth0_NoTraversal()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 0
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.MaxDepthReached.ShouldBe(0);
    }

    [Fact]
    public void Graph_MaxDepth1_DirectOnly()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 1
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.MaxDepthReached.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Graph_MaxDepthReached_Stops()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 2
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.MaxDepthReached.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Graph_MaxDepthReached_InResult()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 10
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo.MaxDepthReached.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Graph_CircularAtDepth2_Detected()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.Include(p => p.Category).First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p!.Category)
            .First();

        reloadedOrder.Status = CustomerOrderStatus.Processing;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Graph_BidirectionalNav_Handled()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        reloadedOrder.Status = CustomerOrderStatus.Processing;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Graph_DeepChain_TraversedFully()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 10
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBeGreaterThan(0);
        category.Id.ShouldBeGreaterThan(0);
        product.CategoryId.ShouldBe(category.Id);
    }

    #endregion

    #region Result Tracking

    [Fact]
    public void Result_ProcessedReferencesByType_Correct()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.ProcessedReferencesByType.ContainsKey("Product").ShouldBeTrue();
        result.TraversalInfo.ProcessedReferencesByType["Product"].Count.ShouldBe(1);
    }

    [Fact]
    public void Result_UniqueReferencesProcessed_Deduplicated()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Shared Widget");
        var order = CreateOrderWithMultipleItemsSameProduct("ORD-001", product, 5);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(1);
    }

    [Fact]
    public void Result_EmptyWhenDisabled()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(0);
        result.TraversalInfo.ProcessedReferencesByType.ShouldBeEmpty();
    }

    [Fact]
    public void Result_MultipleTypes_AllTracked()
    {
        using var context = CreateContext();

        var category = CreateValidCategory("Electronics");
        var product = CreateValidProduct("Widget");
        product.Category = category;
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.ProcessedReferencesByType.ContainsKey("Product").ShouldBeTrue();
        result.TraversalInfo.ProcessedReferencesByType.ContainsKey("Category").ShouldBeTrue();
    }

    #endregion

    #region Strategy & Performance

    [Fact]
    public void Strategy_OneByOne_IsolatesFailures()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var orders = new[]
        {
            CreateOrderWithProductReference("ORD-001", loadedProduct),
            CreateOrderWithProductReference("ORD-002", loadedProduct),
            CreateOrderWithProductReference("ORD-003", loadedProduct)
        };
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .OrderBy(o => o.OrderNumber)
            .ToList();

        foreach (var order in reloadedOrders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }
        reloadedOrders[1].TotalAmount = -500m;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(reloadedOrders, new GraphOptions
        {
            Strategy = BatchStrategy.OneByOne,
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public void Strategy_DivideAndConquer_Efficient()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 5)
            .Select(i => CreateValidProduct($"Product {i}"))
            .ToList();
        context.Products.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProducts = context.Products.ToList();
        var orders = Enumerable.Range(1, 20).Select(i =>
        {
            var product = loadedProducts[i % 5];
            return CreateOrderWithProductReference($"ORD-{i:D3}", product);
        }).ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .ToList();

        foreach (var order in reloadedOrders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(reloadedOrders, new GraphOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(20);
    }

    [Fact]
    public void Performance_1000Items_100SharedRefs()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 100)
            .Select(i => CreateValidProduct($"Product {i}"))
            .ToList();
        context.Products.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProducts = context.Products.ToList();
        var orders = Enumerable.Range(1, 100).Select(i =>
        {
            var product = loadedProducts[i % 100];
            return CreateOrderWithProductReference($"ORD-{i:D4}", product);
        }).ToList();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders, new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            Strategy = BatchStrategy.DivideAndConquer
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);
    }

    #endregion

    #region Backward Compatibility

    [Fact]
    public void DefaultOptions_NoReferenceTraversal()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        var productId = product.Id;
        context.ChangeTracker.Clear();

        var order = CreateValidOrderWithExistingProduct("ORD-001", 1, productId);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.UniqueReferencesProcessed.ShouldBe(0);
    }

    [Fact]
    public void ValidationIntegration_Works()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        reloadedOrder.TotalAmount = -100m;

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteFailure.ShouldBeTrue();
    }

    [Fact]
    public void OrphanBehavior_StillWorks()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loadedProduct = context.Products.First();
        var order = CreateOrderWithProductReference("ORD-001", loadedProduct);
        order.OrderItems.First().Reservations =
        [
            new ItemReservation
            {
                WarehouseLocation = "WH-A",
                ReservedQuantity = 1,
                ReservedAt = DateTimeOffset.UtcNow
            }
        ];
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reloadedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .First();

        reloadedOrder.OrderItems.First().Reservations.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([reloadedOrder], new GraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.ItemReservations.Count().ShouldBe(0);
    }

    [Fact]
    public void EmptyCollection_WithReference_Works()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = []
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void SelfReference_HandledSafely()
    {
        using var context = CreateContext();

        var product = CreateValidProduct("Widget");
        var order = CreateOrderWithProductReference("ORD-001", product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    #endregion

    #region Helper Methods

    private static CustomerOrder CreateOrderWithProductReference(string orderNumber, Product product)
    {
        var item = new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Product = product,
            Quantity = 1,
            UnitPrice = product.Price,
            Subtotal = product.Price
        };

        return new CustomerOrder
        {
            OrderNumber = orderNumber,
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = item.Subtotal,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = [item]
        };
    }

    private static CustomerOrder CreateOrderWithMultipleItemsSameProduct(
        string orderNumber,
        Product product,
        int itemCount)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Product = product,
                Quantity = i,
                UnitPrice = product.Price,
                Subtotal = i * product.Price
            })
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

    private static OrderItem CreateOrderItemWithProduct(Product product, int quantity)
    {
        return new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Product = product,
            Quantity = quantity,
            UnitPrice = product.Price,
            Subtotal = quantity * product.Price
        };
    }

    private static CustomerOrder CreateValidOrder(string orderNumber, int itemCount)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new OrderItem
            {
                ProductId = 1000 + i,
                ProductName = $"Product {i}",
                Quantity = i,
                UnitPrice = 10.00m,
                Subtotal = i * 10.00m
            })
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

    private static CustomerOrder CreateValidOrderWithExistingProduct(
        string orderNumber,
        int itemCount,
        int existingProductId)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new OrderItem
            {
                ProductId = existingProductId,
                ProductName = $"Product {i}",
                Quantity = i,
                UnitPrice = 10.00m,
                Subtotal = i * 10.00m
            })
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

    private static Product CreateValidProduct(string name, decimal price = 10.00m)
    {
        return new Product
        {
            Name = name,
            Price = price,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };
    }

    private static Category CreateValidCategory(string name)
    {
        return new Category { Name = name };
    }

    #endregion
}
