namespace EfCoreUtils;

/// <summary>
/// Represents a composite primary key with proper equality semantics.
/// Used by BatchSaver when auto-detecting keys for entities with multiple key properties.
/// </summary>
/// <remarks>
/// Creates a composite key from multiple values.
/// </remarks>
public readonly struct CompositeKey(params object[] values) : IEquatable<CompositeKey>
{
    private readonly object[] _values = values ?? throw new ArgumentNullException(nameof(values));
    private readonly int _hashCode = ComputeHashCode(values);

    /// <summary>
    /// Gets the key values in order.
    /// </summary>
    public IReadOnlyList<object> Values => _values ?? [];

    /// <summary>
    /// Gets the number of key components.
    /// </summary>
    public int Count => _values?.Length ?? 0;

    /// <summary>
    /// Gets a key component by index.
    /// </summary>
    public object this[int index] => _values[index];

    /// <summary>
    /// Gets a key component as the specified type.
    /// </summary>
    public T GetValue<T>(int index) => (T)_values[index];

    public bool Equals(CompositeKey other)
    {
        if (_values is null && other._values is null)
        {
            return true;
        }

        if (_values is null || other._values is null)
        {
            return false;
        }

        return _values.SequenceEqual(other._values);
    }

    public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString() =>
        _values is null ? "()" : $"({string.Join(", ", _values)})";

    public static bool operator ==(CompositeKey left, CompositeKey right) => left.Equals(right);

    public static bool operator !=(CompositeKey left, CompositeKey right) => !left.Equals(right);

    private static int ComputeHashCode(object[] values)
    {
        var hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
