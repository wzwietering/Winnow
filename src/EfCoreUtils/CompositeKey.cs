namespace EfCoreUtils;

/// <summary>
/// Represents a composite primary key with proper equality semantics.
/// Used by BatchSaver when auto-detecting keys for entities with multiple key properties.
/// </summary>
/// <remarks>
/// Creates a composite key from multiple values.
/// </remarks>
public readonly struct CompositeKey : IEquatable<CompositeKey>
{
    private readonly object[] _values;
    private readonly int _hashCode;

    public CompositeKey(params object[] values)
    {
        _values = ValidateAndCopy(values);
        _hashCode = ComputeHashCode(_values);
    }

    /// <summary>
    /// Gets the key values in order.
    /// </summary>
    public IReadOnlyList<object> Values => _values;

    /// <summary>
    /// Gets the number of key components.
    /// </summary>
    public int Count => _values.Length;

    /// <summary>
    /// Gets a key component by index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public object this[int index]
    {
        get
        {
            if (index < 0 || index >= _values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. CompositeKey has {_values.Length} component(s).");
            }

            return _values[index];
        }
    }

    /// <summary>
    /// Gets a key component as the specified type.
    /// Supports automatic conversion for compatible types (e.g., int to long).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    /// <exception cref="InvalidCastException">Thrown when the component cannot be cast to the specified type.</exception>
    public T GetValue<T>(int index)
    {
        var value = this[index];

        if (value is T typedValue)
        {
            return typedValue;
        }

        return TryConvertValue<T>(value, index);
    }

    /// <summary>
    /// Returns true if this is a single-component key.
    /// </summary>
    public bool IsSingle => _values.Length == 1;

    /// <summary>
    /// Gets the single key value as the specified type.
    /// Use this for cleaner extraction when working with single-component keys via the auto-detect API.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key has multiple components.</exception>
    /// <exception cref="InvalidCastException">Thrown when value cannot be converted to T.</exception>
    public T AsSingle<T>()
    {
        if (_values.Length != 1)
        {
            throw new InvalidOperationException(
                $"Cannot use AsSingle() on composite key with {_values.Length} components. " +
                "Use GetValue<T>(index) to access individual components.");
        }

        return GetValue<T>(0);
    }

    private static T TryConvertValue<T>(object value, int index)
    {
        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // Fall through to throw descriptive error
            }
        }

        throw new InvalidCastException(
            $"Cannot convert key component at index {index} from {value.GetType().Name} to {typeof(T).Name}.");
    }

    public bool Equals(CompositeKey other)
    {
        if (_hashCode != other._hashCode) return false;
        if (_values.Length != other._values.Length) return false;

        for (var i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString() => $"({string.Join(", ", _values)})";

    public static bool operator ==(CompositeKey left, CompositeKey right) => left.Equals(right);

    public static bool operator !=(CompositeKey left, CompositeKey right) => !left.Equals(right);

    /// <summary>
    /// Deconstructs a 2-part composite key.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key does not have exactly 2 components.</exception>
    public void Deconstruct(out object first, out object second)
    {
        ValidateDeconstructCount(2);
        first = _values[0];
        second = _values[1];
    }

    /// <summary>
    /// Deconstructs a 3-part composite key.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key does not have exactly 3 components.</exception>
    public void Deconstruct(out object first, out object second, out object third)
    {
        ValidateDeconstructCount(3);
        first = _values[0];
        second = _values[1];
        third = _values[2];
    }

    /// <summary>
    /// Deconstructs a 4-part composite key.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key does not have exactly 4 components.</exception>
    public void Deconstruct(out object first, out object second, out object third, out object fourth)
    {
        ValidateDeconstructCount(4);
        first = _values[0];
        second = _values[1];
        third = _values[2];
        fourth = _values[3];
    }

    private void ValidateDeconstructCount(int expectedCount)
    {
        if (_values.Length != expectedCount)
        {
            throw new InvalidOperationException(
                $"Cannot deconstruct into {expectedCount} components. " +
                $"CompositeKey has {_values.Length} component(s).");
        }
    }

    private static object[] ValidateAndCopy(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Composite key must have at least one component.", nameof(values));
        }

        var copy = new object[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            copy[i] = values[i] ?? throw new ArgumentException(
                $"Composite key component at index {i} cannot be null.", nameof(values));
        }
        return copy;
    }

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
