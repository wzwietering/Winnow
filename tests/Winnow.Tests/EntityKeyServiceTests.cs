using Winnow.Internal.Services;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Shouldly;

namespace Winnow.Tests;

public class EntityKeyServiceTests : TestBase
{
    #region GetEntityId Tests - Simple Keys

    [Fact]
    public void GetEntityId_IntKey_ReturnsCorrectValue()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var keyService = new EntityKeyService<Product, int>(context);
        var result = keyService.GetEntityId(product);

        result.ShouldBe(42);
    }

    [Fact]
    public void GetEntityId_LongKey_ReturnsCorrectValue()
    {
        using var context = CreateContext();
        var product = new ProductLong { Id = 123456789L, Name = "Test", Price = 10m, Stock = 5 };
        context.ProductLongs.Add(product);

        var keyService = new EntityKeyService<ProductLong, long>(context);
        var result = keyService.GetEntityId(product);

        result.ShouldBe(123456789L);
    }

    [Fact]
    public void GetEntityId_GuidKey_ReturnsCorrectValue()
    {
        using var context = CreateContext();
        var guid = Guid.NewGuid();
        var product = new ProductGuid { Id = guid, Name = "Test", Price = 10m, Stock = 5 };
        context.ProductGuids.Add(product);

        var keyService = new EntityKeyService<ProductGuid, Guid>(context);
        var result = keyService.GetEntityId(product);

        result.ShouldBe(guid);
    }

    [Fact]
    public void GetEntityId_StringKey_ReturnsCorrectValue()
    {
        using var context = CreateContext();
        var product = new ProductString { Id = "PROD-001", Name = "Test", Price = 10m, Stock = 5 };
        context.ProductStrings.Add(product);

        var keyService = new EntityKeyService<ProductString, string>(context);
        var result = keyService.GetEntityId(product);

        result.ShouldBe("PROD-001");
    }

    #endregion

    #region GetEntityId Tests - Composite Keys

    [Fact]
    public void GetEntityId_TwoPartCompositeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var keyService = new EntityKeyService<OrderLine, CompositeKey>(context);
        var result = keyService.GetEntityId(orderLine);

        result.Count.ShouldBe(2);
        result.GetValue<int>(0).ShouldBe(1);
        result.GetValue<int>(1).ShouldBe(2);
    }

    [Fact]
    public void GetEntityId_ThreePartMixedTypeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var location = new InventoryLocation
        {
            WarehouseCode = "WH01",
            AisleNumber = 3,
            BinCode = "A01",
            Quantity = 100,
            LastUpdated = DateTime.UtcNow
        };
        context.InventoryLocations.Add(location);

        var keyService = new EntityKeyService<InventoryLocation, CompositeKey>(context);
        var result = keyService.GetEntityId(location);

        result.Count.ShouldBe(3);
        result.GetValue<string>(0).ShouldBe("WH01");
        result.GetValue<int>(1).ShouldBe(3);
        result.GetValue<string>(2).ShouldBe("A01");
    }

    #endregion

    #region GetEntityId Tests - Auto-Detect Mode

    [Fact]
    public void GetEntityId_AutoDetect_SimpleKey_WrapsInCompositeKey()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var keyService = new EntityKeyService<Product, CompositeKey>(context);
        var result = keyService.GetEntityId(product);

        result.IsSingle.ShouldBeTrue();
        result.AsSingle<int>().ShouldBe(42);
    }

    [Fact]
    public void GetEntityId_AutoDetect_CompositeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var keyService = new EntityKeyService<OrderLine, CompositeKey>(context);
        var result = keyService.GetEntityId(orderLine);

        result.Count.ShouldBe(2);
        result.GetValue<int>(0).ShouldBe(1);
        result.GetValue<int>(1).ShouldBe(2);
    }

    #endregion

    #region GetEntityIdFromEntry Tests

    [Fact]
    public void GetEntityIdFromEntry_SimpleKey_ReturnsCorrectValue()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var keyService = new EntityKeyService<Product, int>(context);
        var entry = context.Entry(product);
        var result = keyService.GetEntityIdFromEntry(entry);

        result.ShouldBe(42);
    }

    [Fact]
    public void GetEntityIdFromEntry_CompositeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var keyService = new EntityKeyService<OrderLine, CompositeKey>(context);
        var entry = context.Entry(orderLine);
        var result = keyService.GetEntityIdFromEntry(entry);

        result.Count.ShouldBe(2);
        result.GetValue<int>(0).ShouldBe(1);
        result.GetValue<int>(1).ShouldBe(2);
    }

    #endregion

    #region CreateEntityKey Tests

    [Fact]
    public void CreateEntityKey_SimpleKey_ReturnsTupleWithTypeName()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var keyService = new EntityKeyService<Product, int>(context);
        var entry = context.Entry(product);
        (string typeName, int id) = keyService.CreateEntityKey(entry);

        typeName.ShouldBe("Product");
        id.ShouldBe(42);
    }

    [Fact]
    public void CreateEntityKey_CompositeKey_ReturnsTupleWithTypeName()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var keyService = new EntityKeyService<OrderLine, CompositeKey>(context);
        var entry = context.Entry(orderLine);
        (string typeName, CompositeKey id) = keyService.CreateEntityKey(entry);

        typeName.ShouldBe("OrderLine");
        id.GetValue<int>(0).ShouldBe(1);
        id.GetValue<int>(1).ShouldBe(2);
    }

    #endregion

    #region Error Condition Tests

    [Fact]
    public void GetEntityId_WrongKeyType_ThrowsDescriptiveError()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var keyService = new EntityKeyService<Product, long>(context);

        var ex = Should.Throw<InvalidOperationException>(() => keyService.GetEntityId(product));
        ex.Message.ShouldContain("Primary key type mismatch");
        ex.Message.ShouldContain("Product");
        ex.Message.ShouldContain("Int64");
        ex.Message.ShouldContain("Int32");
    }

    [Fact]
    public void GetEntityId_CompositeKeyWithSimpleType_ThrowsDescriptiveError()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var keyService = new EntityKeyService<OrderLine, int>(context);

        var ex = Should.Throw<InvalidOperationException>(() => keyService.GetEntityId(orderLine));
        ex.Message.ShouldContain("composite primary key");
        ex.Message.ShouldContain("OrderLine");
    }

    [Fact]
    public void GetEntityId_AutoDetect_NullKey_EfCoreRejectsTracking()
    {
        using var context = CreateContext();
        var product = new ProductString { Id = null!, Name = "Test", Price = 10m, Stock = 5 };

        // EF Core rejects tracking entities with null primary keys before our code runs
        var ex = Should.Throw<InvalidOperationException>(() => context.ProductStrings.Add(product));
        ex.Message.ShouldContain("null");
    }

    [Fact]
    public void GetEntityId_CompositeKey_NullComponent_EfCoreRejectsTracking()
    {
        using var context = CreateContext();
        var location = new InventoryLocation
        {
            WarehouseCode = null!,
            AisleNumber = 3,
            BinCode = "A01",
            Quantity = 100,
            LastUpdated = DateTime.UtcNow
        };

        // EF Core rejects tracking entities with null key components before our code runs
        var ex = Should.Throw<InvalidOperationException>(() => context.InventoryLocations.Add(location));
        ex.Message.ShouldContain("null");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetEntityId_MultipleEntitiesWithDifferentIds_ReturnsCorrectValues()
    {
        using var context = CreateContext();
        var product1 = new Product { Id = 1, Name = "Product 1", Price = 10m, Stock = 5 };
        var product2 = new Product { Id = 2, Name = "Product 2", Price = 20m, Stock = 10 };
        context.Products.AddRange(product1, product2);

        var keyService = new EntityKeyService<Product, int>(context);

        keyService.GetEntityId(product1).ShouldBe(1);
        keyService.GetEntityId(product2).ShouldBe(2);
    }

    [Fact]
    public void CreateEntityKey_DifferentEntityTypes_ReturnsCorrectTypeNames()
    {
        using var context = CreateContext();
        var product = new Product { Id = 1, Name = "Test", Price = 10m, Stock = 5 };
        var category = new Category { Id = 1, Name = "Electronics" };
        context.Products.Add(product);
        context.Categories.Add(category);

        var productKeyService = new EntityKeyService<Product, int>(context);
        var categoryKeyService = new EntityKeyService<Category, int>(context);

        (string productType, int _) = productKeyService.CreateEntityKey(context.Entry(product));
        (string categoryType, int _) = categoryKeyService.CreateEntityKey(context.Entry(category));

        productType.ShouldBe("Product");
        categoryType.ShouldBe("Category");
    }

    #endregion
}
