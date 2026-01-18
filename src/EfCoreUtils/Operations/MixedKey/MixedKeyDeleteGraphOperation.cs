using EfCoreUtils.MixedKey;

namespace EfCoreUtils.Operations.MixedKey;

/// <summary>
/// Delete operation behavior for entity graphs with mixed key types.
/// Handles cascade behavior and child tracking.
/// </summary>
internal class MixedKeyDeleteGraphOperation<TEntity> : IMixedKeyBatchOperation<TEntity>
    where TEntity : class
{
    private readonly DeleteGraphBatchOptions _options;
    private readonly List<MixedKeyId> _successfulIds = [];
    private readonly List<MixedKeyBatchFailure> _failures = [];
    private readonly List<MixedKeyGraphNode> _graphHierarchy = [];
    private readonly Dictionary<MixedKeyId, (MixedKeyGraphNode Node, MixedKeyGraphTraversalResult Stats)> _pendingGraphNodes = [];

    // Stats aggregation
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];
    private readonly Dictionary<Type, int> _entitiesByKeyType = [];

    internal MixedKeyDeleteGraphOperation(DeleteGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, MixedKeyBatchStrategyContext<TEntity> context)
    {
        if (_options.CascadeBehavior == DeleteCascadeBehavior.Throw)
        {
            foreach (var entity in entities)
            {
                context.ValidateCascadeBehaviorRecursive(entity, _options.MaxDepth, _options);
            }
        }
    }

    public void PrepareEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);

        // CRITICAL: Build graph hierarchy BEFORE marking as deleted
        var (node, stats) = context.BuildMixedKeyGraphHierarchy(entity, _options.MaxDepth);
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

    public void RecordSuccess(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context)
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

    public void RecordFailure(TEntity entity, Exception ex, MixedKeyBatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        _pendingGraphNodes.Remove(entityId);

        var failure = new MixedKeyBatchFailure
        {
            EntityId = entityId,
            ErrorMessage = $"Graph delete failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context) =>
        context.DetachEntityGraphRecursive(entity, _options.MaxDepth);

    public MixedKeyBatchResult CreateResult() => new()
    {
        SuccessfulIds = _successfulIds,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = CreateTraversalInfo()
    };

    private void AggregateStats(MixedKeyGraphTraversalResult stats)
    {
        _totalEntitiesTraversed += stats.TotalEntitiesTraversed;
        _maxDepthReached = Math.Max(_maxDepthReached, stats.MaxDepthReached);

        foreach (var (depth, count) in stats.EntitiesByDepth)
        {
            _entitiesByDepth.TryGetValue(depth, out var existing);
            _entitiesByDepth[depth] = existing + count;
        }

        foreach (var (keyType, count) in stats.EntitiesByKeyType)
        {
            _entitiesByKeyType.TryGetValue(keyType, out var existing);
            _entitiesByKeyType[keyType] = existing + count;
        }
    }

    private MixedKeyGraphTraversalResult CreateTraversalInfo() => new()
    {
        MaxDepthReached = _maxDepthReached,
        TotalEntitiesTraversed = _totalEntitiesTraversed,
        EntitiesByDepth = _entitiesByDepth,
        EntitiesByKeyType = _entitiesByKeyType
    };

    private static FailureReason ClassifyException(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        Microsoft.EntityFrameworkCore.DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
