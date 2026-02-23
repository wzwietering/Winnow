using Winnow.Internal;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace Winnow.Tests;

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

    #region CompositeKey.IsAllDefaults Tests

    [Fact]
    public void IsAllDefaults_AllIntZeros_ReturnsTrue()
    {
        var key = new CompositeKey(0, 0);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_IntAndGuidDefaults_ReturnsTrue()
    {
        var key = new CompositeKey(0, Guid.Empty);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_NonDefaultInt_ReturnsFalse()
    {
        var key = new CompositeKey(1, Guid.Empty);
        key.IsAllDefaults().ShouldBeFalse();
    }

    [Fact]
    public void IsAllDefaults_NonDefaultGuid_ReturnsFalse()
    {
        var key = new CompositeKey(0, Guid.NewGuid());
        key.IsAllDefaults().ShouldBeFalse();
    }

    [Fact]
    public void IsAllDefaults_EmptyString_ReturnsTrue()
    {
        var key = new CompositeKey(0, string.Empty);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_NonEmptyString_ReturnsFalse()
    {
        var key = new CompositeKey(0, "abc");
        key.IsAllDefaults().ShouldBeFalse();
    }

    [Fact]
    public void IsAllDefaults_AllNumericTypeDefaults_ReturnsTrue()
    {
        var key = new CompositeKey(0, 0L, (short)0, (byte)0);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_NonDefaultLong_ReturnsFalse()
    {
        var key = new CompositeKey(0, 1L);
        key.IsAllDefaults().ShouldBeFalse();
    }

    [Fact]
    public void IsAllDefaults_SingleComponent_DefaultInt_ReturnsTrue()
    {
        var key = new CompositeKey(0);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_DefaultDecimal_FallbackPath_ReturnsTrue()
    {
        var key = new CompositeKey(0, 0m);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void IsAllDefaults_NonDefaultDecimal_FallbackPath_ReturnsFalse()
    {
        var key = new CompositeKey(0, 1.5m);
        key.IsAllDefaults().ShouldBeFalse();
    }

    [Fact]
    public void DefaultStruct_Count_ReturnsZero()
    {
        var key = default(CompositeKey);
        key.Count.ShouldBe(0);
    }

    [Fact]
    public void DefaultStruct_Values_ReturnsEmpty()
    {
        var key = default(CompositeKey);
        key.Values.Count.ShouldBe(0);
    }

    [Fact]
    public void DefaultStruct_IsAllDefaults_ReturnsTrue()
    {
        var key = default(CompositeKey);
        key.IsAllDefaults().ShouldBeTrue();
    }

    [Fact]
    public void DefaultStruct_IsSingle_ReturnsFalse()
    {
        var key = default(CompositeKey);
        key.IsSingle.ShouldBeFalse();
    }

    [Fact]
    public void DefaultStruct_ToString_ReturnsEmptyParens()
    {
        var key = default(CompositeKey);
        key.ToString().ShouldBe("()");
    }

    [Fact]
    public void DefaultStruct_Equals_DefaultStruct_ReturnsTrue()
    {
        var a = default(CompositeKey);
        var b = default(CompositeKey);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void DefaultStruct_GetHashCode_DoesNotThrow()
    {
        var key = default(CompositeKey);
        Should.NotThrow(() => key.GetHashCode());
    }

    [Fact]
    public void DefaultStruct_Indexer_Throws()
    {
        var key = default(CompositeKey);
        Should.Throw<ArgumentOutOfRangeException>(() => _ = key[0]);
    }

    [Fact]
    public void DefaultStruct_AsSingle_Throws()
    {
        var key = default(CompositeKey);
        Should.Throw<InvalidOperationException>(() => key.AsSingle<int>());
    }

    [Fact]
    public void DefaultStruct_Deconstruct2_Throws()
    {
        var key = default(CompositeKey);
        Should.Throw<InvalidOperationException>(() => key.Deconstruct(out _, out _));
    }

    [Fact]
    public void Deconstruct3_ValidKey_ReturnsComponents()
    {
        var key = new CompositeKey("A", 2, "C");
        key.Deconstruct(out var first, out var second, out var third);
        first.ShouldBe("A");
        second.ShouldBe(2);
        third.ShouldBe("C");
    }

    [Fact]
    public void Deconstruct4_ValidKey_ReturnsComponents()
    {
        var key = new CompositeKey(1, 2, 3, 4);
        key.Deconstruct(out var a, out var b, out var c, out var d);
        a.ShouldBe(1);
        b.ShouldBe(2);
        c.ShouldBe(3);
        d.ShouldBe(4);
    }

    [Fact]
    public void Deconstruct3_WrongCount_Throws()
    {
        var key = new CompositeKey(1, 2);
        Should.Throw<InvalidOperationException>(() => key.Deconstruct(out _, out _, out _));
    }

    [Fact]
    public void Deconstruct4_WrongCount_Throws()
    {
        var key = new CompositeKey(1, 2, 3);
        Should.Throw<InvalidOperationException>(() => key.Deconstruct(out _, out _, out _, out _));
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
