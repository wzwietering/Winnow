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
    private readonly object[] _values = ValidateValues(values);
    private readonly int _hashCode = ComputeHashCode(values);

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
        var value = GetValueAtIndex(index);

        if (value is T typedValue)
        {
            return typedValue;
        }

        return TryConvertValue<T>(value, index);
    }

    /// <summary>
    /// Returns true if this is a simple (single-component) key.
    /// </summary>
    public bool IsSimple => _values.Length == 1;

    /// <summary>
    /// Gets the single key value as the specified type.
    /// Use this for cleaner extraction when working with simple keys via the auto-detect API.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key has multiple components.</exception>
    /// <exception cref="InvalidCastException">Thrown when value cannot be converted to T.</exception>
    public T AsSimple<T>()
    {
        if (_values.Length != 1)
        {
            throw new InvalidOperationException(
                $"Cannot use AsSimple() on composite key with {_values.Length} components. " +
                "Use GetValue<T>(index) to access individual components.");
        }

        return GetValue<T>(0);
    }

    private object GetValueAtIndex(int index)
    {
        if (index < 0 || index >= _values.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range. CompositeKey has {_values.Length} component(s).");
        }

        return _values[index];
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

    public bool Equals(CompositeKey other) => _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString() => $"({string.Join(", ", _values)})";

    public static bool operator ==(CompositeKey left, CompositeKey right) => left.Equals(right);

    public static bool operator !=(CompositeKey left, CompositeKey right) => !left.Equals(right);

    private static object[] ValidateValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Composite key must have at least one component.", nameof(values));
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                throw new ArgumentException($"Composite key component at index {i} cannot be null.", nameof(values));
            }
        }
        return values;
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
