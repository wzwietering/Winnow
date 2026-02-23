using Winnow;
using Winnow.Tests.Entities;
using Shouldly;

namespace Winnow.Tests.CompositeKeyIntegration;

public class CompositeKeyCrudTests : CompositeKeyTestBase
{
    [Fact]
    public void Insert_TwoPartCompositeKey_Success()
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

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(k => k.GetValue<int>(0) == orderId);
    }

    [Fact]
    public void Insert_ThreePartMixedTypeKey_Success()
    {
        using var context = CreateContext();

        var locations = new[]
        {
            new InventoryLocation { WarehouseCode = "WH01", AisleNumber = 1, BinCode = "A01", Quantity = 100, LastUpdated = DateTime.UtcNow },
            new InventoryLocation { WarehouseCode = "WH01", AisleNumber = 1, BinCode = "A02", Quantity = 50, LastUpdated = DateTime.UtcNow },
            new InventoryLocation { WarehouseCode = "WH02", AisleNumber = 2, BinCode = "B01", Quantity = 75, LastUpdated = DateTime.UtcNow }
        };

        var saver = new Winnower<InventoryLocation, CompositeKey>(context);
        var result = saver.Insert(locations);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        var firstKey = result.InsertedIds[0];
        firstKey.GetValue<string>(0).ShouldBe("WH01");
        firstKey.GetValue<int>(1).ShouldBe(1);
        firstKey.GetValue<string>(2).ShouldBe("A01");
    }

    [Fact]
    public void Update_CompositeKey_TracksSuccessfulIds()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        foreach (var line in orderLinesToUpdate)
        {
            line.Quantity += 1;
        }

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Update(orderLinesToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.SuccessfulIds.ShouldAllBe(k => k.GetValue<int>(0) == orderId);
    }

    [Fact]
    public void Update_CompositeKey_PartialFailure_TracksFailedIds()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        orderLinesToUpdate[0].Quantity = 10;
        orderLinesToUpdate[1].Quantity = -5; // Invalid: will fail validation
        orderLinesToUpdate[2].Quantity = 15;

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Update(orderLinesToUpdate);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(new CompositeKey(orderId, 2));
    }

    [Fact]
    public void Delete_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        var expectedKeys = orderLinesToDelete.Select(ol => new CompositeKey(ol.OrderId, ol.LineNumber)).ToList();

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Delete(orderLinesToDelete);

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
    public void Delete_ThreePartKey_Success()
    {
        using var context = CreateContext();
        InsertInventoryLocations(context, 3);

        var locationsToDelete = context.InventoryLocations.ToList();

        var saver = new Winnower<InventoryLocation, CompositeKey>(context);
        var result = saver.Delete(locationsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.InventoryLocations.Count().ShouldBe(0);
    }

    [Fact]
    public void Insert_DuplicateCompositeKey_TracksAsFailure()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = new[]
        {
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = null, Quantity = 5, UnitPrice = 10.00m },
            new OrderLine { OrderId = orderId, LineNumber = 1, ProductId = 2, Quantity = 3, UnitPrice = 15.00m } // Duplicate key
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public void Insert_CompositeKey_LargeBatch_AllSucceed()
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

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.Insert(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);
    }
}
