using EfCoreUtils;
using Shouldly;

namespace EfCoreUtils.Tests.CompositeKeyIntegration;

public class CompositeKeyStructTests
{
    [Fact]
    public void Equality_SameValues_ReturnsTrue()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 2);

        key1.Equals(key2).ShouldBeTrue();
        (key1 == key2).ShouldBeTrue();
    }

    [Fact]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 3);

        key1.Equals(key2).ShouldBeFalse();
        (key1 != key2).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_SameValuesProduceSameHash()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(1, 2);

        key1.GetHashCode().ShouldBe(key2.GetHashCode());
    }

    [Fact]
    public void ToString_FormatsReadably()
    {
        var key = new CompositeKey(1, 2);

        key.ToString().ShouldBe("(1, 2)");
    }

    [Fact]
    public void GetValue_ReturnsTypedComponent()
    {
        var key = new CompositeKey("WH01", 5, "BIN-A");

        key.GetValue<string>(0).ShouldBe("WH01");
        key.GetValue<int>(1).ShouldBe(5);
        key.GetValue<string>(2).ShouldBe("BIN-A");
    }

    [Fact]
    public void GetValue_ConvertsCompatibleTypes()
    {
        var key = new CompositeKey(42, 100);

        key.GetValue<long>(0).ShouldBe(42L);
        key.GetValue<double>(1).ShouldBe(100.0);
    }

    [Fact]
    public void IsSingle_SingleComponent_ReturnsTrue()
    {
        var key = new CompositeKey(42);

        key.IsSingle.ShouldBeTrue();
    }

    [Fact]
    public void IsSingle_MultipleComponents_ReturnsFalse()
    {
        var key = new CompositeKey(1, 2);

        key.IsSingle.ShouldBeFalse();
    }

    [Fact]
    public void AsSingle_SingleComponent_ReturnsTypedValue()
    {
        var key = new CompositeKey(42);

        key.AsSingle<int>().ShouldBe(42);
    }

    [Fact]
    public void AsSingle_SingleComponent_ConvertsCompatibleTypes()
    {
        var key = new CompositeKey(42);

        key.AsSingle<long>().ShouldBe(42L);
    }

    [Fact]
    public void DifferentOrder_NotEqual()
    {
        var key1 = new CompositeKey(1, 2);
        var key2 = new CompositeKey(2, 1);

        key1.ShouldNotBe(key2);
        key1.GetHashCode().ShouldNotBe(key2.GetHashCode());
    }

    [Fact]
    public void ArrayMutation_DoesNotAffectKey()
    {
        var values = new object[] { 1, 2 };
        var key = new CompositeKey(values);
        var originalHash = key.GetHashCode();

        values[0] = 999; // Mutate original array

        key[0].ShouldBe(1); // Key unchanged
        key.GetHashCode().ShouldBe(originalHash);
    }

    [Fact]
    public void BoxedValues_AreEqual()
    {
        var key1 = new CompositeKey(1, 2);
        object[] values = { 1, 2 };
        var key2 = new CompositeKey(values);

        key1.ShouldBe(key2);
        key1.GetHashCode().ShouldBe(key2.GetHashCode());
    }

    [Fact]
    public void Values_ReturnsSameReference()
    {
        var key = new CompositeKey(1, 2, 3);
        var values1 = key.Values;
        var values2 = key.Values;

        ReferenceEquals(values1, values2).ShouldBeTrue();
    }

    [Fact]
    public void Deconstruct_TwoParts_Works()
    {
        var key = new CompositeKey(1, "test");
        var (first, second) = key;

        first.ShouldBe(1);
        second.ShouldBe("test");
    }

    [Fact]
    public void Deconstruct_ThreeParts_Works()
    {
        var key = new CompositeKey("WH01", 5, "BIN-A");
        var (first, second, third) = key;

        first.ShouldBe("WH01");
        second.ShouldBe(5);
        third.ShouldBe("BIN-A");
    }

    [Fact]
    public void Deconstruct_FourParts_Works()
    {
        var key = new CompositeKey(1, 2, 3, 4);
        var (first, second, third, fourth) = key;

        first.ShouldBe(1);
        second.ShouldBe(2);
        third.ShouldBe(3);
        fourth.ShouldBe(4);
    }
}
