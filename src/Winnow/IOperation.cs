using Winnow.Internal.Accumulators;
using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Defines operation-specific behavior for batch processing strategies.
/// Strategies (OneByOne, DivideAndConquer) use this interface to delegate
/// operation-specific logic (Update, Insert, Delete) while reusing the algorithm.
/// </summary>
internal interface IOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>The configured pre-validation options (or null if none).</summary>
    ValidationOptions? Validation { get; }

    /// <summary>The accumulator used to record per-entity outcomes.</summary>
    WinnowAccumulator<TKey> Accumulator { get; }

    /// <summary>
    /// True for the four graph operations. Used to gate
    /// <see cref="ValidationOptions.IncludeNavigations"/>, which is documented as
    /// having no effect on flat operations.
    /// </summary>
    bool IsGraphOperation => false;

    /// <summary>
    /// Optional navigation filter forwarded to the pre-validation navigation walk
    /// so excluded navigations are not validated, matching the scope of the graph
    /// operation. Always <c>null</c> for flat operations.
    /// </summary>
    NavigationFilter? NavigationFilter => null;

    /// <summary>
    /// Runs the configured pre-validation pipeline (if any) and returns the
    /// entities that should continue into the strategy. Failures are recorded
    /// into the operation's accumulator; survivors keep their order.
    /// When no validation is configured, returns the input list unchanged.
    /// </summary>
    List<TEntity> ApplyPreValidation(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken) =>
        OperationPreValidationHelper.Run(Validation, entities, context, Accumulator, IsGraphOperation, NavigationFilter, cancellationToken);

    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Prepares a single entity for the database operation (e.g., sets entity state).
    /// </summary>
    void PrepareEntity(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a successful operation after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a failed operation after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, Exception ex, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    WinnowResult<TKey> CreateResult(bool wasCancelled = false);
}

/// <summary>
/// Defines operation-specific behavior for insert batch processing strategies.
/// Unlike IOperation, tracks entities by index since they don't have IDs before insertion.
/// </summary>
internal interface IInsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>The configured pre-validation options (or null if none).</summary>
    ValidationOptions? Validation { get; }

    /// <summary>The accumulator used to record per-entity insert outcomes.</summary>
    InsertAccumulator<TKey> Accumulator { get; }

    /// <summary>
    /// True for the four graph operations. Used to gate
    /// <see cref="ValidationOptions.IncludeNavigations"/>, which is documented as
    /// having no effect on flat operations.
    /// </summary>
    bool IsGraphOperation => false;

    /// <summary>
    /// Optional navigation filter forwarded to the pre-validation navigation walk
    /// so excluded navigations are not validated, matching the scope of the graph
    /// operation. Always <c>null</c> for flat operations.
    /// </summary>
    NavigationFilter? NavigationFilter => null;

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
        OperationPreValidationHelper.RunIndexed(Validation, entities, context, Accumulator, IsGraphOperation, NavigationFilter, cancellationToken);

    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Prepares a single entity for the insert operation.
    /// </summary>
    void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a successful insert after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a failed insert after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    InsertResult<TKey> CreateResult(bool wasCancelled = false);
}
