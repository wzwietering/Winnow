using EfCoreUtils.Internal;

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
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly GraphStatisticsTracker<TKey> _statsTracker = new();

    internal InsertGraphOperation(InsertGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences &&
            _options.CircularReferenceHandling == CircularReferenceHandling.Throw)
        {
            foreach (var entity in entities)
            {
                context.ValidateCircularReferences(entity, _options.MaxDepth);
            }
        }

        if (_options.IncludeManyToMany)
        {
            context.ValidateManyToManyEntitiesExistBatched(entities, _options);
        }
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsAddedWithReferences(
                entity, _options.MaxDepth, _options.CircularReferenceHandling);
            _statsTracker.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsAddedRecursive(entity, _options.MaxDepth);
        }

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ProcessManyToManyForInsert(entity, _options);
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);

        _insertedEntities.Add(new InsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });

        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _options.MaxDepth)
            : context.BuildGraphHierarchy(entity, _options.MaxDepth);
        _graphHierarchy.Add(node);
        _statsTracker.AggregateStats(stats);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var failure = new InsertBatchFailure
        {
            EntityIndex = index,
            ErrorMessage = $"Graph insert failed: {ex.Message}",
            Reason = FailureClassifier.Classify(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) => context.DetachEntityGraphRecursive(entity, _options.MaxDepth);

    public InsertBatchResult<TKey> CreateResult() => new()
    {
        InsertedEntities = _insertedEntities,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = _statsTracker.CreateTraversalInfo()
    };
}
