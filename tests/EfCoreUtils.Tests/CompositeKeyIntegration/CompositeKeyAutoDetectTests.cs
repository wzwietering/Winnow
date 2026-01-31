using EfCoreUtils;
using EfCoreUtils.Tests.Entities;
using Shouldly;

namespace EfCoreUtils.Tests.CompositeKeyIntegration;

public class CompositeKeyAutoDetectTests : CompositeKeyTestBase
{
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
}
