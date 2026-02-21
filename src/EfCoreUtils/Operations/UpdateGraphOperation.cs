using EfCoreUtils.Internal;

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
    private readonly TraversalContext _tc;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<BatchFailure<TKey>> _failures = [];
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];
    private readonly GraphStatisticsTracker<TKey> _statsTracker = new();

    internal UpdateGraphOperation(GraphBatchOptions options)
    {
        _options = options;
        _tc = TraversalContext.FromOptions(options);
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        NavigationFilterValidator.Validate(
            _tc.NavigationFilter, context.Context.Model, _options.IncludeReferences, _options.IncludeManyToMany);

        context.CaptureAllOriginalChildIdsRecursive(entities, _tc);

        if (_options.IncludeManyToMany)
        {
            context.CaptureOriginalManyToManyLinks(entities, _tc);
        }

        foreach (var entity in entities)
        {
            context.ValidateNoOrphanedChildrenRecursive(entity, _tc, _options);

            if (_options.IncludeReferences)
            {
                context.ValidateCircularReferences(entity, _tc);
            }
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsModifiedWithReferences(entity, _tc);
            _statsTracker.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsModifiedRecursive(entity, _tc);
        }

        context.HandleOrphanedChildrenRecursive(entity, _tc, _options.OrphanedChildBehavior);

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ApplyManyToManyChanges(entity, _options);
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }

        var entityId = context.GetEntityId(entity);
        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _pendingGraphNodes[entityId] = (node, stats);
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
            ErrorMessage = $"Graph update failed: {ex.Message}",
            Reason = FailureClassifier.Classify(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) =>
        context.DetachEntityWithOrphansRecursive(entity, _tc);

    public BatchResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        SuccessfulIds = _successfulIds,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = _statsTracker.CreateTraversalInfo(),
        WasCancelled = wasCancelled
    };
}
