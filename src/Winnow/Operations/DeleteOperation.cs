using Microsoft.EntityFrameworkCore;

namespace Winnow.Operations;

/// <summary>
/// Delete operation behavior for parent-only entity deletes.
/// Sets entities to Deleted state and tracks results by entity ID.
/// </summary>
internal class DeleteOperation<TEntity, TKey> : IOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DeleteOptions _options;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<WinnowFailure<TKey>> _failures = [];

    internal DeleteOperation(DeleteOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        if (!_options.ValidateNavigationProperties)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateNoPopulatedNavigationPropertiesForDelete(entity);
        }
    }

    public void PrepareEntity(TEntity entity, StrategyContext<TEntity, TKey> context) => context.AttachEntityAsDeleted(entity);

    public void RecordSuccess(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);
    }

    public void RecordFailure(TEntity entity, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var failure = context.CreateWinnowFailure(entityId, ex);
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) => context.Context.Entry(entity).State = EntityState.Detached;

    public WinnowResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        SuccessfulIds = _successfulIds,
        Failures = _failures,
        WasCancelled = wasCancelled
    };
}
