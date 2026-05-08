using Microsoft.EntityFrameworkCore;
using Winnow.Internal;
using Winnow.Internal.Accumulators;

namespace Winnow.Operations;

/// <summary>
/// Insert operation behavior for parent-only entity inserts.
/// Sets entities to Added state and tracks results by original index.
/// </summary>
internal class InsertOperation<TEntity, TKey> : IInsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly InsertOptions _options;
    private readonly InsertAccumulator<TKey> _accumulator;

    internal InsertOperation(InsertOptions options, InsertAccumulator<TKey> accumulator)
    {
        _options = options;
        _accumulator = accumulator;
    }

    public void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        if (!_options.ValidateNavigationProperties)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateNoPopulatedNavigationProperties(entity);
        }
    }

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context) =>
        context.Context.Entry(entity).State = EntityState.Added;

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _accumulator.RecordSuccess(entityId, index, entity);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordFailure(index, ex.Message, FailureClassifier.Classify(ex), ex);

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        try
        {
            context.Context.Entry(entity).State = EntityState.Detached;
        }
        catch (ArgumentNullException)
        {
            // EF Core 8/9 throws when detaching entities with null keys (e.g., null string PKs).
            // EF Core 10+ handles this gracefully. Cleanup is best-effort, so swallow the error.
        }
    }

    public InsertResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled);
}
