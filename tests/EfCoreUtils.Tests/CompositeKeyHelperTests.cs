using EfCoreUtils.Internal;
using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace EfCoreUtils.Tests;

public class CompositeKeyHelperTests : TestBase
{
    #region ExtractEntityId Tests

    [Fact]
    public void ExtractEntityId_FromEntry_SimpleKey_ReturnsValue()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };
        context.Products.Add(product);

        var entry = context.Entry(product);
        var keyProperties = entry.Metadata.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(entry, keyProperties);

        result.ShouldBe(42);
    }

    [Fact]
    public void ExtractEntityId_FromEntry_CompositeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };
        context.OrderLines.Add(orderLine);

        var entry = context.Entry(orderLine);
        var keyProperties = entry.Metadata.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(entry, keyProperties);

        result.ShouldBeOfType<CompositeKey>();
        var key = (CompositeKey)result!;
        key.Count.ShouldBe(2);
        key.GetValue<int>(0).ShouldBe(1);
        key.GetValue<int>(1).ShouldBe(2);
    }

    [Fact]
    public void ExtractEntityId_FromEntry_ThreePartKey_ReturnsCompositeKey()
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

        var entry = context.Entry(location);
        var keyProperties = entry.Metadata.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(entry, keyProperties);

        result.ShouldBeOfType<CompositeKey>();
        var key = (CompositeKey)result!;
        key.Count.ShouldBe(3);
        key.GetValue<string>(0).ShouldBe("WH01");
        key.GetValue<int>(1).ShouldBe(3);
        key.GetValue<string>(2).ShouldBe("A01");
    }

    [Fact]
    public void ExtractEntityId_FromObject_SimpleKey_ReturnsValue()
    {
        using var context = CreateContext();
        var product = new Product { Id = 42, Name = "Test", Price = 10m, Stock = 5 };

        var entityType = context.Model.FindEntityType(typeof(Product))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(product, keyProperties);

        result.ShouldBe(42);
    }

    [Fact]
    public void ExtractEntityId_FromObject_CompositeKey_ReturnsCompositeKey()
    {
        using var context = CreateContext();
        var orderLine = new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 5, UnitPrice = 10m };

        var entityType = context.Model.FindEntityType(typeof(OrderLine))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(orderLine, keyProperties);

        result.ShouldBeOfType<CompositeKey>();
        var key = (CompositeKey)result!;
        key.GetValue<int>(0).ShouldBe(1);
        key.GetValue<int>(1).ShouldBe(2);
    }

    [Fact]
    public void ExtractEntityId_FromEntry_ZeroValue_ReturnsZero()
    {
        using var context = CreateContext();
        var product = new Product { Name = "Test", Price = 10m, Stock = 5 }; // Id not set, defaults to 0

        var entityType = context.Model.FindEntityType(typeof(Product))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.ExtractEntityId(product, keyProperties);
        result.ShouldBe(0);
    }

    #endregion

    #region IsCompatibleKeyType Tests

    [Fact]
    public void IsCompatibleKeyType_NullProperties_ReturnsFalse()
    {
        var result = CompositeKeyHelper.IsCompatibleKeyType<int>(null);

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatibleKeyType_EmptyProperties_ReturnsFalse()
    {
        var emptyList = Array.Empty<IProperty>();

        var result = CompositeKeyHelper.IsCompatibleKeyType<int>(emptyList);

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatibleKeyType_SimpleKey_MatchingType_ReturnsTrue()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Product))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<int>(keyProperties);

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatibleKeyType_SimpleKey_WrongType_ReturnsFalse()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Product))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<long>(keyProperties);

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatibleKeyType_CompositeKey_WithCompositeKeyType_ReturnsTrue()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(OrderLine))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<CompositeKey>(keyProperties);

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatibleKeyType_CompositeKey_WithSimpleType_ReturnsFalse()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(OrderLine))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<int>(keyProperties);

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatibleKeyType_GuidKey_ReturnsTrue()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ProductGuid))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<Guid>(keyProperties);

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatibleKeyType_StringKey_ReturnsTrue()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ProductString))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        var result = CompositeKeyHelper.IsCompatibleKeyType<string>(keyProperties);

        result.ShouldBeTrue();
    }

    #endregion

    #region ForeignKeyMatchesParent Tests

    [Fact]
    public void ForeignKeyMatchesParent_SimpleKey_Match_ReturnsTrue()
    {
        using var context = CreateContext();
        var note = new OrderLineNote { OrderId = 1, LineNumber = 1, Note = "Test", CreatedAt = DateTime.UtcNow };
        context.OrderLineNotes.Add(note);

        var entry = context.Entry(note);
        var entityType = context.Model.FindEntityType(typeof(OrderLineNote))!;
        var fkProperty = entityType.GetProperties().First(p => p.Name == "OrderId");

        var result = CompositeKeyHelper.ForeignKeyMatchesParent(entry, [fkProperty], 1);

        result.ShouldBeTrue();
    }

    [Fact]
    public void ForeignKeyMatchesParent_SimpleKey_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();
        var note = new OrderLineNote { OrderId = 1, LineNumber = 1, Note = "Test", CreatedAt = DateTime.UtcNow };
        context.OrderLineNotes.Add(note);

        var entry = context.Entry(note);
        var entityType = context.Model.FindEntityType(typeof(OrderLineNote))!;
        var fkProperty = entityType.GetProperties().First(p => p.Name == "OrderId");

        var result = CompositeKeyHelper.ForeignKeyMatchesParent(entry, [fkProperty], 999);

        result.ShouldBeFalse();
    }

    [Fact]
    public void ForeignKeyMatchesParent_CompositeKey_Match_ReturnsTrue()
    {
        using var context = CreateContext();
        var note = new OrderLineNote { OrderId = 1, LineNumber = 2, Note = "Test", CreatedAt = DateTime.UtcNow };
        context.OrderLineNotes.Add(note);

        var entry = context.Entry(note);
        var entityType = context.Model.FindEntityType(typeof(OrderLineNote))!;
        var fkProperties = entityType.GetProperties()
            .Where(p => p.Name == "OrderId" || p.Name == "LineNumber")
            .OrderBy(p => p.Name == "OrderId" ? 0 : 1) // OrderId first
            .ToList();

        var parentKey = new CompositeKey(1, 2);
        var result = CompositeKeyHelper.ForeignKeyMatchesParent(entry, fkProperties, parentKey);

        result.ShouldBeTrue();
    }

    [Fact]
    public void ForeignKeyMatchesParent_CompositeKey_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();
        var note = new OrderLineNote { OrderId = 1, LineNumber = 2, Note = "Test", CreatedAt = DateTime.UtcNow };
        context.OrderLineNotes.Add(note);

        var entry = context.Entry(note);
        var entityType = context.Model.FindEntityType(typeof(OrderLineNote))!;
        var fkProperties = entityType.GetProperties()
            .Where(p => p.Name == "OrderId" || p.Name == "LineNumber")
            .OrderBy(p => p.Name == "OrderId" ? 0 : 1)
            .ToList();

        var parentKey = new CompositeKey(1, 99); // Wrong line number
        var result = CompositeKeyHelper.ForeignKeyMatchesParent(entry, fkProperties, parentKey);

        result.ShouldBeFalse();
    }

    #endregion
}
