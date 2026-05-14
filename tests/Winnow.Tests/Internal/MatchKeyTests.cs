using Shouldly;
using Winnow.Internal;

namespace Winnow.Tests.Internal;

/// <summary>
/// MatchKey is the dictionary key for the entire MatchBy resolution. Equality bugs
/// silently misroute entities (insert instead of update). These tests guard the contract.
/// </summary>
public class MatchKeyTests
{
    [Fact]
    public void Equals_TwoKeysWithIdenticalValues_AreEqual()
    {
        var a = new MatchKey(new object?[] { "tenant-1", 42 });
        var b = new MatchKey(new object?[] { "tenant-1", 42 });

        a.Equals(b).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_TwoKeysWithIdenticalValues_ProduceSameHash()
    {
        var a = new MatchKey(new object?[] { "tenant-1", 42 });
        var b = new MatchKey(new object?[] { "tenant-1", 42 });

        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferingScalarValues_AreNotEqual()
    {
        var a = new MatchKey(new object?[] { "tenant-1", 42 });
        var b = new MatchKey(new object?[] { "tenant-1", 43 });

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_NullVsValue_InSameSlot_AreNotEqual()
    {
        var a = new MatchKey(new object?[] { "tenant-1", null });
        var b = new MatchKey(new object?[] { "tenant-1", 42 });

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_DifferingArities_AreNotEqual()
    {
        var a = new MatchKey(new object?[] { "tenant-1" });
        var b = new MatchKey(new object?[] { "tenant-1", 42 });

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_DifferentTypesInSlot_AreNotEqual()
    {
        var a = new MatchKey(new object?[] { 1 });
        var b = new MatchKey(new object?[] { 1L });

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void DictionaryLookup_TwoArrayInstancesWithSameValues_HitSameBucket()
    {
        var dict = new Dictionary<MatchKey, string>
        {
            [new MatchKey(new object?[] { "abc", 1 })] = "found"
        };

        dict.TryGetValue(new MatchKey(new object?[] { "abc", 1 }), out var value).ShouldBeTrue();
        value.ShouldBe("found");
    }

    [Fact]
    public void ContainsNull_AnyNullElement_ReturnsTrue()
    {
        MatchKey.ContainsNull(new object?[] { "a", null, 1 }).ShouldBeTrue();
    }

    [Fact]
    public void ContainsNull_AllNonNull_ReturnsFalse()
    {
        MatchKey.ContainsNull(new object?[] { "a", 1 }).ShouldBeFalse();
    }

    [Fact]
    public void ContainsNull_EmptyArray_ReturnsFalse()
    {
        MatchKey.ContainsNull(Array.Empty<object?>()).ShouldBeFalse();
    }
}
