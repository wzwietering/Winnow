using Microsoft.EntityFrameworkCore;

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
    private readonly List<InsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<InsertFailure> _failures = [];

    internal InsertOperation(InsertOptions options) => _options = options;

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

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context) => context.Context.Entry(entity).State = EntityState.Added;

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _insertedEntities.Add(new InsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var failure = context.CreateInsertFailure(index, ex);
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) => context.Context.Entry(entity).State = EntityState.Detached;

    public InsertResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        InsertedEntities = _insertedEntities,
        Failures = _failures,
        WasCancelled = wasCancelled
    };
}
