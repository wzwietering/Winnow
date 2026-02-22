namespace Winnow;

/// <summary>
/// Configuration for transient failure retry behavior.
/// Note: The retry handler re-invokes SaveChanges on the same DbContext.
/// For providers that do not automatically recover entity state after transient failures,
/// consider using the provider's built-in retry strategy (e.g. EnableRetryOnFailure) instead.
/// </summary>
public sealed class RetryOptions
{
    private int _maxRetries = 3;
    private TimeSpan _initialDelay = TimeSpan.FromMilliseconds(100);
    private double _backoffMultiplier = 2.0;

    /// <summary>
    /// Maximum number of retry attempts. Default: 3. Must be non-negative.
    /// </summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxRetries = value;
        }
    }

    /// <summary>
    /// Initial delay before the first retry. Default: 100ms.
    /// Subsequent retries use exponential backoff.
    /// </summary>
    public TimeSpan InitialDelay
    {
        get => _initialDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _initialDelay = value;
        }
    }

    /// <summary>
    /// Multiplier applied to delay between retries. Default: 2.0. Must be positive.
    /// </summary>
    public double BackoffMultiplier
    {
        get => _backoffMultiplier;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _backoffMultiplier = value;
        }
    }

    /// <summary>
    /// Custom predicate to determine if an exception is transient.
    /// When null (default), uses the built-in classifier.
    /// When set, replaces the built-in classifier entirely.
    /// </summary>
    public Func<Exception, bool>? IsTransient { get; set; }
}
