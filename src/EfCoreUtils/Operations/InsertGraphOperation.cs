namespace EfCoreUtils.Operations;

/// <summary>
/// Insert operation behavior for entity graphs (parent + children).
/// EF Core handles FK propagation automatically when graph is attached as Added.
/// </summary>
internal class InsertGraphOperation<TEntity, TKey> : IBatchInsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly InsertGraphBatchOptions _options;
    private readonly List<InsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<InsertBatchFailure> _failures = [];
    private readonly Dictionary<TKey, IReadOnlyList<TKey>> _childIdsByParentId = [];

    internal InsertGraphOperation(InsertGraphBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        // No validation needed for graph inserts - we expect children
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        context.AttachEntityGraphAsAdded(entity);
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var childIds = context.GetChildIds(entity);

        _insertedEntities.Add(new InsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });

        _childIdsByParentId[entityId] = childIds;
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var failure = new InsertBatchFailure
        {
            EntityIndex = index,
            ErrorMessage = $"Graph insert failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        context.DetachEntityGraph(entity);
    }

    public InsertBatchResult<TKey> CreateResult()
    {
        return new InsertBatchResult<TKey>
        {
            InsertedEntities = _insertedEntities,
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
