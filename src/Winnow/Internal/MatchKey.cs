using System.Collections;

namespace Winnow.Internal;

/// <summary>
/// Structural-equality key composed of one or more match-expression values.
/// Used as a dictionary key when looking up existing entities by MatchBy values.
/// </summary>
/// <remarks>
/// Unlike <see cref="CompositeKey"/> this internal struct permits null components,
/// matching the semantics of business-key match values that may be nullable.
/// </remarks>
internal readonly struct MatchKey : IEquatable<MatchKey>
{
    private readonly object?[]? _values;

    internal MatchKey(object?[] values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    internal int Length => _values!.Length;

    internal object? GetValue(int index) => _values![index];

    public bool Equals(MatchKey other) => HaveSameValues(_values, other._values);

    public override bool Equals(object? obj) => obj is MatchKey other && Equals(other);

    public override int GetHashCode()
    {
        if (_values is null) return 0;
        var hc = new HashCode();
        foreach (var value in _values)
        {
            hc.Add(ValueHashCode(value));
        }
        return hc.ToHashCode();
    }

    private static bool HaveSameValues(object?[]? left, object?[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!ValueEquals(left[i], right[i])) return false;
        }
        return true;
    }

    // Equality and hashing for individual match-value slots. Arrays (notably byte[] used
    // for hash/binary business keys) fall back to reference equality under object.Equals,
    // which would silently misroute every entity to INSERT. Use structural comparison so
    // two distinct array instances with identical contents are treated as equal.
    private static bool ValueEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is IStructuralEquatable se && a.GetType() == b.GetType())
        {
            return se.Equals(b, StructuralComparisons.StructuralEqualityComparer);
        }
        return a.Equals(b);
    }

    private static int ValueHashCode(object? value)
    {
        if (value is null) return 0;
        if (value is IStructuralEquatable se)
        {
            return se.GetHashCode(StructuralComparisons.StructuralEqualityComparer);
        }
        return value.GetHashCode();
    }

    public static bool operator ==(MatchKey left, MatchKey right) => left.Equals(right);
    public static bool operator !=(MatchKey left, MatchKey right) => !left.Equals(right);

    /// <summary>
    /// Returns true if any element of <paramref name="tuple"/> is null. Match-value tuples
    /// containing nulls are treated as "no business key" and routed to insert.
    /// </summary>
    internal static bool ContainsNull(object?[] tuple)
    {
        foreach (var v in tuple)
        {
            if (v is null) return true;
        }
        return false;
    }
}
