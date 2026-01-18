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
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];

    // Stats aggregation
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];

    // Reference tracking
    private readonly Dictionary<string, List<TKey>> _processedReferencesByType = [];
    private int _maxReferenceDepthReached;

    internal UpdateGraphOperation(GraphBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        context.CaptureAllOriginalChildIdsRecursive(entities, _options.MaxDepth);

        foreach (var entity in entities)
        {
            context.ValidateNoOrphanedChildrenRecursive(entity, _options.MaxDepth, _options);

            if (_options.IncludeReferences &&
                _options.CircularReferenceHandling == CircularReferenceHandling.Throw)
            {
                context.ValidateCircularReferences(entity, _options.MaxDepth);
            }
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsModifiedWithReferences(
                entity, _options.MaxDepth, _options.CircularReferenceHandling);
            AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsModifiedRecursive(entity, _options.MaxDepth);
        }

        context.HandleOrphanedChildrenRecursive(entity, _options.MaxDepth, _options.OrphanedChildBehavior);

        var entityId = context.GetEntityId(entity);
        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _options.MaxDepth)
            : context.BuildGraphHierarchy(entity, _options.MaxDepth);
        _pendingGraphNodes[entityId] = (node, stats);
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
            ErrorMessage = $"Graph update failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) => context.DetachEntityWithOrphansRecursive(entity, _options.MaxDepth);

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

    private GraphTraversalResult<TKey> CreateTraversalInfo()
    {
        var processedRefs = _processedReferencesByType.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<TKey>)kvp.Value.AsReadOnly());

        return new GraphTraversalResult<TKey>
        {
            MaxDepthReached = _maxDepthReached,
            TotalEntitiesTraversed = _totalEntitiesTraversed,
            EntitiesByDepth = _entitiesByDepth,
            ProcessedReferencesByType = processedRefs,
            UniqueReferencesProcessed = _processedReferencesByType.Values.Sum(list => list.Count),
            MaxReferenceDepthReached = _maxReferenceDepthReached
        };
    }

    private void AggregateReferenceStats(EfCoreUtils.Internal.Services.ReferenceTrackingResult refResult)
    {
        foreach (var (typeName, ids) in refResult.ProcessedReferencesByType)
        {
            if (!_processedReferencesByType.TryGetValue(typeName, out var list))
            {
                list = [];
                _processedReferencesByType[typeName] = list;
            }

            foreach (var id in ids)
            {
                if (id is TKey typedId)
                {
                    list.Add(typedId);
                }
            }
        }
        _maxReferenceDepthReached = Math.Max(_maxReferenceDepthReached, refResult.MaxReferenceDepthReached);
    }

    private static FailureReason ClassifyException(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        Microsoft.EntityFrameworkCore.DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
