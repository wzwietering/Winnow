namespace EfCoreUtils.MixedKey;

/// <summary>
/// Represents an entity ID that can be of any key type.
/// Provides type-safe access to the underlying value.
/// </summary>
public readonly struct MixedKeyId : IEquatable<MixedKeyId>
{
    private readonly object _value;

    /// <summary>
    /// The CLR type of the key value.
    /// </summary>
    public Type KeyType { get; }

    /// <summary>
    /// Creates a new mixed key ID with the specified value and type.
    /// </summary>
    public MixedKeyId(object value, Type keyType)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
    }

    /// <summary>
    /// Gets the key value as the specified type.
    /// Throws if the type doesn't match.
    /// </summary>
    public TKey GetValue<TKey>() where TKey : notnull
    {
        if (typeof(TKey) != KeyType)
        {
            throw new InvalidOperationException(
                $"Key type mismatch. Requested {typeof(TKey).Name}, actual type is {KeyType.Name}.");
        }

        return (TKey)_value;
    }

    /// <summary>
    /// Attempts to get the key value as the specified type.
    /// Returns false if the type doesn't match.
    /// </summary>
    public bool TryGetValue<TKey>(out TKey? value) where TKey : notnull
    {
        if (typeof(TKey) == KeyType)
        {
            value = (TKey)_value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the key value as an object.
    /// </summary>
    public object GetValueAsObject() => _value;

    /// <inheritdoc />
    public override string ToString() => _value?.ToString() ?? string.Empty;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MixedKeyId other && Equals(other);

    /// <inheritdoc />
    public bool Equals(MixedKeyId other)
    {
        return KeyType == other.KeyType && Equals(_value, other._value);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(KeyType, _value);

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(MixedKeyId left, MixedKeyId right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(MixedKeyId left, MixedKeyId right) => !left.Equals(right);
}
