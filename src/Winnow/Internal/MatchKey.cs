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
            hc.Add(value);
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
            if (!Equals(left[i], right[i])) return false;
        }
        return true;
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
