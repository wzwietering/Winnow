namespace EfCoreUtils.Operations;

/// <summary>
/// Update operation behavior for entity graphs (parent + children).
/// Handles orphan detection, child tracking, and graph attachment.
/// </summary>
internal class UpdateGraphOperation<TEntity, TKey> : IBatchOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly GraphBatchOptions _options;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<BatchFailure<TKey>> _failures = [];
    private readonly Dictionary<TKey, IReadOnlyList<TKey>> _childIdsByParentId = [];
    private readonly Dictionary<TKey, IReadOnlyList<TKey>> _pendingChildIds = [];

    internal UpdateGraphOperation(GraphBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        context.CaptureAllOriginalChildIds(entities);

        foreach (var entity in entities)
        {
            context.ValidateNoOrphanedChildren(entity, _options);
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        context.AttachEntityGraphAsModified(entity);
        context.HandleOrphanedChildren(entity, _options);

        var entityId = context.GetEntityId(entity);
        var childIds = context.GetChildIds(entity);
        _pendingChildIds[entityId] = childIds;
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);

        if (_pendingChildIds.TryGetValue(entityId, out var childIds))
        {
            _childIdsByParentId[entityId] = childIds;
            _pendingChildIds.Remove(entityId);
        }
    }

    public void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _pendingChildIds.Remove(entityId);

        var failure = new BatchFailure<TKey>
        {
            EntityId = entityId,
            ErrorMessage = $"Graph update failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        context.DetachEntityWithOrphans(entity);
    }

    public BatchResult<TKey> CreateResult()
    {
        return new BatchResult<TKey>
        {
            SuccessfulIds = _successfulIds,
            Failures = _failures,
            ChildIdsByParentId = _childIdsByParentId
        };
    }

    private static FailureReason ClassifyException(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            Microsoft.EntityFrameworkCore.DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };
    }
}
