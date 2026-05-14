using System.Buffers;
using System.Runtime.CompilerServices;

namespace Winnow;

/// <summary>
/// Stack-only buffer that a <see cref="ValidatorDelegate{TEntity}"/> uses to report
/// zero, one, or many <see cref="ValidationError"/> instances for a single entity.
/// </summary>
/// <remarks>
/// <para>
/// The collector wraps a caller-supplied buffer of <see cref="InlineCapacity"/>
/// slots (the pipeline allocates one such buffer per batch and reuses it across
/// every entity). It only rents from <see cref="ArrayPool{T}"/> when a single
/// entity emits more than that inline capacity — the typical zero-or-one error
/// case never touches the pool.
/// </para>
/// <para>
/// As a <c>ref struct</c>, this type cannot escape the stack, be captured by a
/// lambda, or be used in async methods — those restrictions are what keep the
/// rented buffer's lifetime correct.
/// </para>
/// </remarks>
public ref struct ValidationCollector
{
    /// <summary>
    /// Inline slot count before the collector falls back to <see cref="ArrayPool{T}"/>.
    /// Sized to cover the overwhelming majority of validators (typically 0–4 errors).
    /// </summary>
    public const int InlineCapacity = 4;

    private Span<ValidationError> _buffer;
    private ValidationError[]? _rented;
    private int _count;

    /// <summary>
    /// Creates a collector backed by a caller-supplied span. The pipeline passes
    /// the per-batch inline buffer here; user code should not construct
    /// collectors directly.
    /// </summary>
    internal ValidationCollector(Span<ValidationError> initialBuffer)
    {
        _buffer = initialBuffer;
        _rented = null;
        _count = 0;
    }

    /// <summary>
    /// Number of errors currently held.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// True if no errors have been added — equivalent to <c>Count == 0</c>.
    /// </summary>
    public readonly bool IsValid => _count == 0;

    /// <summary>
    /// Records a <see cref="ValidationError"/> for the entity currently being validated.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ValidationError error)
    {
        if (_count == _buffer.Length)
        {
            Grow();
        }
        _buffer[_count++] = error;
    }

    /// <summary>
    /// Records a <see cref="ValidationError"/> built from the supplied property name and message.
    /// Equivalent to <c>Add(new ValidationError(propertyName, message))</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(string propertyName, string message) =>
        Add(new ValidationError(propertyName, message));

    /// <summary>
    /// Records a <see cref="ValidationError"/> built from a property name, message, and machine code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(string propertyName, string message, string code) =>
        Add(new ValidationError(propertyName, message, code));

    /// <summary>
    /// Returns the recorded errors as a read-only span. The span is valid only
    /// for the duration of the validator invocation; copy it if you need to outlive that scope.
    /// </summary>
    public readonly ReadOnlySpan<ValidationError> AsSpan() => _buffer[.._count];

    private void Grow()
    {
        var newSize = checked(_buffer.Length * 2);
        var newRented = ArrayPool<ValidationError>.Shared.Rent(newSize);
        _buffer.CopyTo(newRented);
        if (_rented is not null)
        {
            ArrayPool<ValidationError>.Shared.Return(_rented, clearArray: true);
        }
        _rented = newRented;
        _buffer = newRented;
    }

    /// <summary>
    /// Returns any rented buffer to <see cref="ArrayPool{T}"/>. The pipeline calls
    /// this exactly once per entity validated; user code should not invoke it.
    /// </summary>
    internal void Dispose()
    {
        if (_rented is null)
        {
            return;
        }
        ArrayPool<ValidationError>.Shared.Return(_rented, clearArray: true);
        _rented = null;
        _buffer = default;
        _count = 0;
    }
}
