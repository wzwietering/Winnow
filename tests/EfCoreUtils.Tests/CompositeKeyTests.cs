using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class CompositeKeyTests : TestBase
{
    #region Category 1: CompositeKey Struct

    [Fact]
    public void CompositeKey_Equality_SameValues_ReturnsTrue()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 2);

        key1.Equals(key2).ShouldBeTrue();
        (key1 == key2).ShouldBeTrue();
    }

    [Fact]
    public void CompositeKey_Equality_DifferentValues_ReturnsFalse()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 3);

        key1.Equals(key2).ShouldBeFalse();
        (key1 != key2).ShouldBeTrue();
    }

    [Fact]
    public void CompositeKey_GetHashCode_SameValuesProduceSameHash()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 2);

        key1.GetHashCode().ShouldBe(key2.GetHashCode());
    }

    [Fact]
    public void CompositeKey_ToString_FormatsReadably()
    {
        var key = new CompositeKey(1, 2);

        key.ToString().ShouldBe("(1, 2)");
    }

    [Fact]
    public void CompositeKey_GetValue_ReturnsTypedComponent()
    {
        var key = new CompositeKey("WH01", 5, "BIN-A");

        key.GetValue<string>(0).ShouldBe("WH01");
        key.GetValue<int>(1).ShouldBe(5);
        key.GetValue<string>(2).ShouldBe("BIN-A");
    }

    [Fact]
    public void CompositeKey_GetValue_ConvertsCompatibleTypes()
    {
        var key = new CompositeKey(42, 100);

        key.GetValue<long>(0).ShouldBe(42L);
        key.GetValue<double>(1).ShouldBe(100.0);
    }

    [Fact]
    public void CompositeKey_GetValue_IncompatibleType_ThrowsDescriptiveError()
    {
        var key = new CompositeKey("text", 42);

        var ex = Should.Throw<InvalidCastException>(() => key.GetValue<int>(0));
        ex.Message.ShouldContain("String");
        ex.Message.ShouldContain("Int32");
    }

    [Fact]
    public void CompositeKey_IsSimple_SingleComponent_ReturnsTrue()
    {
        var key = new CompositeKey(42);

        key.IsSimple.ShouldBeTrue();
    }

    [Fact]
    public void CompositeKey_IsSimple_MultipleComponents_ReturnsFalse()
    {
        var key = new CompositeKey(1, 2);

        key.IsSimple.ShouldBeFalse();
    }

    [Fact]
    public void CompositeKey_AsSimple_SingleComponent_ReturnsTypedValue()
    {
        var key = new CompositeKey(42);

        key.AsSimple<int>().ShouldBe(42);
    }

    [Fact]
    public void CompositeKey_AsSimple_SingleComponent_ConvertsCompatibleTypes()
    {
        var key = new CompositeKey(42);

        key.AsSimple<long>().ShouldBe(42L);
    }

    [Fact]
    public void CompositeKey_AsSimple_MultipleComponents_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<InvalidOperationException>(() => key.AsSimple<int>());
        ex.Message.ShouldContain("2 components");
        ex.Message.ShouldContain("GetValue<T>(index)");
    }

    #endregion

    #region Category 2: Auto-Detect API

    [Fact]
    public void AutoDetect_SimpleKey_IsCompositeKeyFalse()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product>(context);

        saver.IsCompositeKey.ShouldBeFalse();
    }

    [Fact]
    public void AutoDetect_CompositeKey_IsCompositeKeyTrue()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<OrderLine>(context);

        saver.IsCompositeKey.ShouldBeTrue();
    }

    [Fact]
    public void AutoDetect_SimpleKey_StillWorks()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Test Product",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<Product>(context);
        var result = saver.InsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedIds.Count.ShouldBe(1);
        result.InsertedIds[0].GetValue<int>(0).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void AutoDetect_CompositeKey_Works()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m
        };

        var saver = new BatchSaver<OrderLine>(context);
        var result = saver.InsertBatch([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedIds.Count.ShouldBe(1);
        result.InsertedIds[0].GetValue<int>(0).ShouldBe(orderId);
        result.InsertedIds[0].GetValue<int>(1).ShouldBe(1);
    }

    #endregion

    #region Category 3: Basic CRUD with Composite Keys

    [Fact]
    public void InsertBatch_TwoPartCompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 3).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i * 2,
            UnitPrice = 10.00m + i
        }).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(k => k.GetValue<int>(0) == orderId);
    }

    [Fact]
    public void InsertBatch_ThreePartMixedTypeKey_Success()
    {
        using var context = CreateContext();

        var locations = new[]
        {
            new InventoryLocation { WarehouseCode = "WH01", AisleNumber = 1, BinCode = "A01", Quantity = 100, LastUpdated = DateTime.UtcNow },
            new InventoryLocation { WarehouseCode = "WH01", AisleNumber = 1, BinCode = "A02", Quantity = 50, LastUpdated = DateTime.UtcNow },
            new InventoryLocation { WarehouseCode = "WH02", AisleNumber = 2, BinCode = "B01", Quantity = 75, LastUpdated = DateTime.UtcNow }
        };

        var saver = new BatchSaver<InventoryLocation, CompositeKey>(context);
        var result = saver.InsertBatch(locations);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        var firstKey = result.InsertedIds[0];
        firstKey.GetValue<string>(0).ShouldBe("WH01");
        firstKey.GetValue<int>(1).ShouldBe(1);
        firstKey.GetValue<string>(2).ShouldBe("A01");
    }

    [Fact]
    public void UpdateBatch_CompositeKey_TracksSuccessfulIds()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        foreach (var line in orderLinesToUpdate)
        {
            line.Quantity += 1;
        }

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.UpdateBatch(orderLinesToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.SuccessfulIds.ShouldAllBe(k => k.GetValue<int>(0) == orderId);
    }

    [Fact]
    public void UpdateBatch_CompositeKey_PartialFailure_TracksFailedIds()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        orderLinesToUpdate[0].Quantity = 10;
        orderLinesToUpdate[1].Quantity = -5; // Invalid: will fail validation
        orderLinesToUpdate[2].Quantity = 15;

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.UpdateBatch(orderLinesToUpdate);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(new CompositeKey(orderId, 2));
    }

    [Fact]
    public void DeleteBatch_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        var expectedKeys = orderLinesToDelete.Select(ol => new CompositeKey(ol.OrderId, ol.LineNumber)).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.DeleteBatch(orderLinesToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        foreach (var key in expectedKeys)
        {
            result.SuccessfulIds.ShouldContain(key);
        }

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void DeleteBatch_ThreePartKey_Success()
    {
        using var context = CreateContext();
        InsertInventoryLocations(context, 3);

        var locationsToDelete = context.InventoryLocations.ToList();

        var saver = new BatchSaver<InventoryLocation, CompositeKey>(context);
        var result = saver.DeleteBatch(locationsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.InventoryLocations.Count().ShouldBe(0);
    }

    [Fact]
    public void InsertBatch_DuplicateCompositeKey_TracksAsFailure()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = new[]
        {
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = null, Quantity = 5, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = 2, Quantity = 3, UnitPrice = 15.00m } // Duplicate key
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public void InsertBatch_CompositeKey_LargeBatch_AllSucceed()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 50).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = 1,
            UnitPrice = 10.00m
        }).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);
    }

    #endregion

    #region Category 4: Graph Operations with Composite Keys

    [Fact]
    public void InsertGraphBatch_CompositeParent_WithChildren_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraphBatch([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        loaded.Notes.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraphBatch_CompositeKey_ModifyChild_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        var noteToModify = loaded.Notes.First();
        noteToModify.Note = "Modified Note";

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.UpdateGraphBatch([loaded]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var reloaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        reloaded.Notes.ShouldContain(n => n.Note == "Modified Note");
    }

    [Fact]
    public void UpdateGraphBatch_CompositeKey_OrphanDetection_Works()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        var orderLine = InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        var noteToRemove = loaded.Notes.First();
        loaded.Notes.Remove(noteToRemove);

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var reloaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        reloaded.Notes.Count.ShouldBe(1);
    }

    [Fact]
    public void DeleteGraphBatch_CompositeKey_CascadeDeletes()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.DeleteGraphBatch([loaded]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
        context.OrderLineNotes.Count(n => n.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void GraphHierarchy_TracksCompositeKeys()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraphBatch([orderLine]);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(1);

        var parentKey = new CompositeKey(orderId, 1);
        var parentNode = result.GraphHierarchy.First(n => n.EntityId.Equals(parentKey));
        parentNode.GetChildIds().Count.ShouldBe(2);
    }

    [Fact]
    public void MaxDepth_RespectsCompositeKeyHierarchy()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraphBatch([orderLine], new InsertGraphBatchOptions
        {
            MaxDepth = 0 // Only insert parent, not children
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(1);
        context.OrderLineNotes.Count(n => n.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void MultiLevel_CompositeAndSimpleKeys_Mixed()
    {
        using var context = CreateContext();

        // CustomerOrder (simple key) -> OrderLine (composite key) -> OrderLineNote (simple key)
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-MIX-001",
            CustomerName = "Mixed Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Multi-level test note", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraphBatch([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loadedNote = context.OrderLineNotes
            .First(n => n.OrderId == orderId && n.LineNumber == 1);
        loadedNote.Id.ShouldBeGreaterThan(0);
    }

    #endregion

    #region Category 5: Strategy Comparison

    [Fact]
    public void OneByOne_CompositeKey_IsolatesFailures()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = new[]
        {
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = null, Quantity = 5, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 2, ProductId = null, Quantity = -1, UnitPrice = 10.00m }, // Invalid - negative quantity
            new OrderLine { OrderId = orderId, LineNumber = 3, ProductId = null, Quantity = 3, UnitPrice = 10.00m }
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines, new InsertBatchOptions { Strategy = BatchStrategy.OneByOne });

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

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines, new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

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

        var oneByOneSaver = new BatchSaver<OrderLine, CompositeKey>(context1);
        var oneByOneResult = oneByOneSaver.InsertBatch(createLines(orderId1, 5), new InsertBatchOptions { Strategy = BatchStrategy.OneByOne });

        var divideAndConquerSaver = new BatchSaver<OrderLine, CompositeKey>(context2);
        var divideAndConquerResult = divideAndConquerSaver.InsertBatch(createLines(orderId2, 5), new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        oneByOneResult.SuccessCount.ShouldBe(divideAndConquerResult.SuccessCount);
        oneByOneResult.FailureCount.ShouldBe(divideAndConquerResult.FailureCount);
        oneByOneResult.Failures.Select(f => f.EntityIndex).ShouldBe(divideAndConquerResult.Failures.Select(f => f.EntityIndex));
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

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.InsertBatch(orderLines, new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(1); // All succeed in one batch
    }

    #endregion

    #region Category 6: Async Operations

    [Fact]
    public async Task InsertBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 3).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i * 2,
            UnitPrice = 10.00m + i
        }).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.InsertBatchAsync(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        foreach (var line in orderLinesToUpdate)
        {
            line.Quantity += 1;
        }

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.UpdateBatchAsync(orderLinesToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
    }

    [Fact]
    public async Task DeleteBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.DeleteBatchAsync(orderLinesToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Async Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Async Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.InsertGraphBatchAsync([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        loaded.Notes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AutoDetect_AsyncOperations_Work()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m
        };

        var saver = new BatchSaver<OrderLine>(context);
        var result = await saver.InsertBatchAsync([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedIds[0].GetValue<int>(0).ShouldBe(orderId);
        result.InsertedIds[0].GetValue<int>(1).ShouldBe(1);
    }

    #endregion

    #region Category 7: Error Scenarios & Edge Cases

    [Fact]
    public void CompositeKey_NullComponent_ThrowsDescriptiveError()
    {
        Should.Throw<ArgumentNullException>(() => new CompositeKey(null!));
    }

    [Fact]
    public void ErrorMessage_CompositeKey_FormatsReadably()
    {
        var key = new CompositeKey(1, 2, "test");
        key.ToString().ShouldBe("(1, 2, test)");
    }

    [Fact]
    public void InsertBatch_CompositeKey_ValidationError_CorrectIndices()
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
    public void DeleteBatch_CompositeKey_VerifyDatabaseState()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 2);

        var linesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        linesToDelete.Count.ShouldBe(2);

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = saver.DeleteBatch(linesToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify database state
        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void CompositeKey_GetValue_OutOfRange_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => key.GetValue<int>(5));
        ex.Message.ShouldContain("out of range");
        ex.Message.ShouldContain("2 component(s)");
    }

    [Fact]
    public void CompositeKey_Indexer_OutOfRange_ThrowsDescriptiveError()
    {
        var key = new CompositeKey(1, 2);

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => _ = key[-1]);
        ex.Message.ShouldContain("out of range");
    }

    [Fact]
    public void CompositeKey_EmptyArray_ThrowsDescriptiveError()
    {
        var ex = Should.Throw<ArgumentException>(() => new CompositeKey(Array.Empty<object>()));
        ex.Message.ShouldContain("at least one component");
    }

    [Fact]
    public void CompositeKey_NullComponentInArray_ThrowsDescriptiveError()
    {
        var ex = Should.Throw<ArgumentException>(() => new CompositeKey(1, null!, 3));
        ex.Message.ShouldContain("cannot be null");
        ex.Message.ShouldContain("index 1");
    }

    #endregion

    #region Helper Methods

    private static int CreateCustomerOrder(TestDbContext context)
    {
        var order = new CustomerOrder
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();
        return orderId;
    }

    private static void InsertOrderLines(TestDbContext context, int orderId, int count)
    {
        var orderLines = Enumerable.Range(1, count).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i,
            UnitPrice = 10.00m + i
        }).ToList();

        context.OrderLines.AddRange(orderLines);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private static void InsertInventoryLocations(TestDbContext context, int count)
    {
        var locations = Enumerable.Range(1, count).Select(i => new InventoryLocation
        {
            WarehouseCode = $"WH{i:D2}",
            AisleNumber = i,
            BinCode = $"BIN-{i:D2}",
            Quantity = i * 10,
            LastUpdated = DateTime.UtcNow
        }).ToList();

        context.InventoryLocations.AddRange(locations);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private static OrderLine InsertOrderLineWithNotes(TestDbContext context, int orderId, int lineNumber, int noteCount)
    {
        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = lineNumber,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes = Enumerable.Range(1, noteCount).Select(i => new OrderLineNote
            {
                Note = $"Note {i}",
                CreatedAt = DateTime.UtcNow
            }).ToList()
        };

        context.OrderLines.Add(orderLine);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return orderLine;
    }

    #endregion
}
