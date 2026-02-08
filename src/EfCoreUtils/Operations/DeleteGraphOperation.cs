using EfCoreUtils.Internal;

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
    private readonly TraversalContext _tc;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<BatchFailure<TKey>> _failures = [];
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];
    private readonly GraphStatisticsTracker<TKey> _statsTracker = new();

    internal DeleteGraphOperation(DeleteGraphBatchOptions options)
    {
        _options = options;
        _tc = TraversalContext.FromOptions(options);
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        NavigationFilterValidator.Validate(
            _tc.NavigationFilter, context.Context.Model, _options.IncludeReferences, _options.IncludeManyToMany);

        foreach (var entity in entities)
        {
            if (_options.CascadeBehavior == DeleteCascadeBehavior.Throw)
            {
                context.ValidateCascadeBehaviorRecursive(entity, _tc, _options);
            }

            if (_options.ValidateReferencedEntitiesExist)
            {
                context.ValidateReferencedEntitiesExist(entity, _tc);
            }
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);

        // CRITICAL: Build graph hierarchy BEFORE marking as deleted
        var (node, stats) = context.BuildGraphHierarchy(entity, _tc);
        _pendingGraphNodes[entityId] = (node, stats);

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ProcessManyToManyForDelete(entity);
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }

        if (_options.CascadeBehavior == DeleteCascadeBehavior.ParentOnly)
        {
            context.AttachEntityAsDeleted(entity);
        }
        else
        {
            context.AttachEntityGraphAsDeletedRecursive(entity, _tc);
        }
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);

        if (_pendingGraphNodes.TryGetValue(entityId, out var pending))
        {
            _graphHierarchy.Add(pending.Node);
            _statsTracker.AggregateStats(pending.Stats);
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
            Reason = FailureClassifier.Classify(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) =>
        context.DetachEntityGraphRecursive(entity, _tc);

    public BatchResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        SuccessfulIds = _successfulIds,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = _statsTracker.CreateTraversalInfo(),
        WasCancelled = wasCancelled
    };
}
