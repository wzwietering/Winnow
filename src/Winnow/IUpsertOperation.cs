using Winnow.Internal.Accumulators;
using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Defines operation-specific behavior for upsert batch processing strategies.
/// Uses index tracking (like IInsertOperation) since some entities may be new.
/// </summary>
/// <remarks>
/// MatchBy-specific hooks (batch pre-SELECT, retry refresh) live on
/// <see cref="IMatchByCapableOperation{TEntity, TKey}"/>; strategies branch via
/// <c>as</c>-cast so operations without MatchBy support carry no no-op stubs.
/// </remarks>
internal interface IUpsertOperation<TEntity, TKey> : IPreValidatable<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>The accumulator used to record per-entity upsert outcomes.</summary>
    UpsertAccumulator<TKey> Accumulator { get; }

    /// <summary>
    /// Runs the configured pre-validation pipeline (if any). Returns the
    /// survivors plus an optional original-index map; the caller uses
    /// <see cref="Winnow.Internal.Validation.PreValidationResult{TEntity}.GetOriginalIndex"/>
    /// to record results against the user-visible input position.
    /// </summary>
    PreValidationResult<TEntity> ApplyPreValidation(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken) =>
        OperationPreValidationHelper.RunIndexed(Validation, entities, context, Accumulator, NavigationFilter, cancellationToken);

    void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context);
    void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
    void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
    void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context);
    void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    UpsertResult<TKey> CreateResult(bool wasCancelled = false);

    /// <summary>
    /// Returns true if the entity at the given index was prepared as an INSERT.
    /// </summary>
    bool WasInsertAttempt(int index);

    /// <summary>
    /// Gets the duplicate key strategy from options.
    /// </summary>
    DuplicateKeyStrategy DuplicateKeyStrategy { get; }

    /// <summary>
    /// Records a successful update after retry (was originally planned as insert).
    /// </summary>
    void RecordSuccessAsUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
}
