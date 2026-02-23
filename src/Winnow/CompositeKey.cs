namespace Winnow;

/// <summary>
/// Represents a composite primary key with proper equality semantics.
/// Used by Winnower when auto-detecting keys for entities with multiple key properties.
/// </summary>
/// <remarks>
/// Creates a composite key from multiple values.
/// </remarks>
public readonly struct CompositeKey : IEquatable<CompositeKey>
{
    private readonly object[] _values;
    private readonly int _hashCode;

    /// <summary>
    /// Creates a composite key from the specified values.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when values is null.</exception>
    /// <exception cref="ArgumentException">Thrown when values is empty or contains null.</exception>
    public CompositeKey(params object[] values)
    {
        _values = ValidateAndCopy(values);
        _hashCode = ComputeHashCode(_values);
    }

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
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public object this[int index]
    {
        get
        {
            if (_values is null || index < 0 || index >= _values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. CompositeKey has {_values?.Length ?? 0} component(s).");
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
    public bool IsSingle => _values?.Length == 1;

    /// <summary>
    /// Gets the single key value as the specified type.
    /// Use this for cleaner extraction when working with single-component keys via the auto-detect API.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when key has multiple components.</exception>
    /// <exception cref="InvalidCastException">Thrown when value cannot be converted to T.</exception>
    public T AsSingle<T>()
    {
        if ((_values?.Length ?? 0) != 1)
        {
            throw new InvalidOperationException(
                $"Cannot use AsSingle() on composite key with {_values?.Length ?? 0} components. " +
                "Use GetValue<T>(index) to access individual components.");
        }

        return GetValue<T>(0);
    }

    /// <summary>
    /// Returns true if all components have their default values.
    /// </summary>
    /// <remarks>
    /// Default values: int/long/short/byte = 0, Guid = Guid.Empty, string = null or empty.
    /// </remarks>
    public bool IsAllDefaults()
    {
        if (_values is null) return true;

        foreach (var value in _values)
        {
            if (!IsDefaultValue(value))
                return false;
        }
        return true;
    }

    private static bool IsDefaultValue(object value)
    {
        return value switch
        {
            int i => i == 0,
            long l => l == 0L,
            short s => s == 0,
            byte b => b == 0,
            Guid g => g == Guid.Empty,
            string str => string.IsNullOrEmpty(str),
            _ when value.GetType().IsValueType => value.Equals(Activator.CreateInstance(value.GetType())),
            _ => false
        };
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

    /// <inheritdoc />
    public bool Equals(CompositeKey other)
    {
        if (_hashCode != other._hashCode) return false;
        if ((_values?.Length ?? 0) != (other._values?.Length ?? 0)) return false;
        if (_values is null) return true;

        for (var i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values![i])) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public override string ToString() => _values is null ? "()" : $"({string.Join(", ", _values)})";

    /// <summary>
    /// Determines whether two composite keys are equal.
    /// </summary>
    public static bool operator ==(CompositeKey left, CompositeKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two composite keys are not equal.
    /// </summary>
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
        if ((_values?.Length ?? 0) != expectedCount)
        {
            throw new InvalidOperationException(
                $"Cannot deconstruct into {expectedCount} components. " +
                $"CompositeKey has {_values?.Length ?? 0} component(s).");
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
