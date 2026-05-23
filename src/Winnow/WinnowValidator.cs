namespace Winnow;

/// <summary>
/// Synchronous, allocation-free validator signature used by pre-validation.
/// The validator runs once per entity; emit zero, one, or many
/// <see cref="ValidationError"/> instances by calling
/// <see cref="ValidationCollector.Add(ValidationError)"/> (or one of its overloads).
/// </summary>
/// <typeparam name="TEntity">The entity type being validated.</typeparam>
/// <param name="entity">The entity instance. Never null when invoked by the pipeline.</param>
/// <param name="collector">
/// A stack-only collector. Do not capture, store, or pass to async methods —
/// the buffer is released back to the pool when the validator returns.
/// </param>
/// <remarks>
/// Validators must not perform I/O, database queries, or otherwise block. The
/// pipeline invokes them serially on the calling thread; expensive validators
/// will dominate batch throughput. For DB-backed checks, prefer
/// <c>Upsert</c> with <c>MatchBy</c> instead of running a query per entity here.
/// </remarks>
public delegate void WinnowValidator<TEntity>(TEntity entity, ref ValidationCollector collector);
