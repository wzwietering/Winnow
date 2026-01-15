namespace EfCoreUtils.Operations;

/// <summary>
/// Delete operation behavior for entity graphs (parent + children).
/// Handles cascade behavior and child tracking.
/// </summary>
internal class DeleteGraphOperation<TEntity> : IBatchOperation<TEntity> where TEntity : class
{
    private readonly DeleteGraphBatchOptions _options;
    private readonly List<int> _successfulIds = [];
    private readonly List<BatchFailure> _failures = [];
    private readonly Dictionary<int, IReadOnlyList<int>> _childIdsByParentId = [];
    private readonly Dictionary<int, IReadOnlyList<int>> _pendingChildIds = [];

    internal DeleteGraphOperation(DeleteGraphBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        if (_options.CascadeBehavior == DeleteCascadeBehavior.Throw)
        {
            foreach (var entity in entities)
            {
                context.ValidateCascadeBehavior(entity, _options);
            }
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        var childIds = context.GetChildIds(entity);
        _pendingChildIds[entityId] = childIds;

        if (_options.CascadeBehavior == DeleteCascadeBehavior.ParentOnly)
        {
            context.AttachEntityAsDeleted(entity);
        }
        else
        {
            context.AttachEntityGraphAsDeleted(entity);
        }
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);

        if (_pendingChildIds.TryGetValue(entityId, out var childIds))
        {
            _childIdsByParentId[entityId] = childIds;
            _pendingChildIds.Remove(entityId);
        }
    }

    public void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        _pendingChildIds.Remove(entityId);

        var failure = new BatchFailure
        {
            EntityId = entityId,
            ErrorMessage = $"Graph delete failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        context.DetachEntityGraph(entity);
    }

    public BatchResult CreateResult()
    {
        return new BatchResult
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
