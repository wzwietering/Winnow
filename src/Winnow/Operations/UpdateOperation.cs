using Microsoft.EntityFrameworkCore;
using Winnow.Internal;
using Winnow.Internal.Accumulators;

namespace Winnow.Operations;

/// <summary>
/// Update operation behavior for parent-only entity updates.
/// Sets entities to Modified state and tracks results by entity ID.
/// </summary>
internal class UpdateOperation<TEntity, TKey> : IOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly WinnowOptions _options;
    private readonly WinnowAccumulator<TKey> _accumulator;

    internal UpdateOperation(WinnowOptions options, WinnowAccumulator<TKey> accumulator)
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
            context.ValidateNoModifiedNavigationProperties(entity);
        }
    }

    public void PrepareEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.Context.Entry(entity).State = EntityState.Modified;

    public void RecordSuccess(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordSuccess(context.GetEntityId(entity));

    public void RecordFailure(TEntity entity, Exception ex, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordFailure(
            context.GetEntityId(entity),
            ex.Message,
            FailureClassifier.Classify(ex),
            ex);

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.Context.Entry(entity).State = EntityState.Detached;

    public WinnowResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled);
}
