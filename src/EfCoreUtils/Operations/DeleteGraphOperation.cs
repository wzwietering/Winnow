namespace EfCoreUtils.Operations;

/// <summary>
/// Delete operation behavior for entity graphs (parent + children).
/// Handles cascade behavior and child tracking.
/// </summary>
internal class DeleteGraphOperation<TEntity, TKey> : IBatchOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DeleteGraphBatchOptions _options;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<BatchFailure<TKey>> _failures = [];
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];

    // Stats aggregation
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];

    internal DeleteGraphOperation(DeleteGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.CascadeBehavior == DeleteCascadeBehavior.Throw)
        {
            foreach (var entity in entities)
            {
                context.ValidateCascadeBehaviorRecursive(entity, _options.MaxDepth, _options);
            }
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);

        // CRITICAL: Build graph hierarchy BEFORE marking as deleted
        var (node, stats) = context.BuildGraphHierarchy(entity, _options.MaxDepth);
        _pendingGraphNodes[entityId] = (node, stats);

        if (_options.CascadeBehavior == DeleteCascadeBehavior.ParentOnly)
        {
            context.AttachEntityAsDeleted(entity);
        }
        else
        {
            context.AttachEntityGraphAsDeletedRecursive(entity, _options.MaxDepth);
        }
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);

        if (_pendingGraphNodes.TryGetValue(entityId, out var pending))
        {
            _graphHierarchy.Add(pending.Node);
            AggregateStats(pending.Stats);
            _pendingGraphNodes.Remove(entityId);
        }
    }

    public void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _pendingGraphNodes.Remove(entityId);

        var failure = new BatchFailure<TKey>
        {
            EntityId = entityId,
            ErrorMessage = $"Graph delete failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) => context.DetachEntityGraphRecursive(entity, _options.MaxDepth);

    public BatchResult<TKey> CreateResult() => new()
    {
        SuccessfulIds = _successfulIds,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = CreateTraversalInfo()
    };

    private void AggregateStats(GraphTraversalResult<TKey> stats)
    {
        _totalEntitiesTraversed += stats.TotalEntitiesTraversed;
        _maxDepthReached = Math.Max(_maxDepthReached, stats.MaxDepthReached);

        foreach (var (depth, count) in stats.EntitiesByDepth)
        {
            _entitiesByDepth.TryGetValue(depth, out var existing);
            _entitiesByDepth[depth] = existing + count;
        }
    }

    private GraphTraversalResult<TKey> CreateTraversalInfo() => new()
    {
        MaxDepthReached = _maxDepthReached,
        TotalEntitiesTraversed = _totalEntitiesTraversed,
        EntitiesByDepth = _entitiesByDepth
    };

    private static FailureReason ClassifyException(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        Microsoft.EntityFrameworkCore.DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
