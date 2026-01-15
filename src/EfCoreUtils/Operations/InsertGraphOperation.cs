namespace EfCoreUtils.Operations;

/// <summary>
/// Insert operation behavior for entity graphs (parent + children).
/// EF Core handles FK propagation automatically when graph is attached as Added.
/// </summary>
internal class InsertGraphOperation<TEntity> : IBatchInsertOperation<TEntity> where TEntity : class
{
    private readonly InsertGraphBatchOptions _options;
    private readonly List<InsertedEntity> _insertedEntities = [];
    private readonly List<InsertBatchFailure> _failures = [];
    private readonly Dictionary<int, IReadOnlyList<int>> _childIdsByParentId = [];

    internal InsertGraphOperation(InsertGraphBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        // No validation needed for graph inserts - we expect children
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity> context)
    {
        context.AttachEntityGraphAsAdded(entity);
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        var childIds = context.GetChildIds(entity);

        _insertedEntities.Add(new InsertedEntity
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });

        _childIdsByParentId[entityId] = childIds;
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity> context)
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

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        context.DetachEntityGraph(entity);
    }

    public InsertBatchResult CreateResult()
    {
        return new InsertBatchResult
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
