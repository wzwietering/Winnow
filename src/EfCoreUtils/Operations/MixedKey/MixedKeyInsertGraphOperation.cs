using EfCoreUtils.MixedKey;

namespace EfCoreUtils.Operations.MixedKey;

/// <summary>
/// Insert operation behavior for entity graphs with mixed key types.
/// EF Core handles FK propagation automatically when graph is attached as Added.
/// </summary>
internal class MixedKeyInsertGraphOperation<TEntity> : IMixedKeyBatchInsertOperation<TEntity>
    where TEntity : class
{
    private readonly InsertGraphBatchOptions _options;
    private readonly List<MixedKeyInsertedEntity> _insertedEntities = [];
    private readonly List<InsertBatchFailure> _failures = [];
    private readonly List<MixedKeyGraphNode> _graphHierarchy = [];

    // Stats aggregation
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];
    private readonly Dictionary<Type, int> _entitiesByKeyType = [];

    internal MixedKeyInsertGraphOperation(InsertGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, MixedKeyBatchStrategyContext<TEntity> context)
    {
        // No validation needed for graph inserts - we expect children
    }

    public void PrepareEntity(TEntity entity, int index, MixedKeyBatchStrategyContext<TEntity> context) =>
        context.AttachEntityGraphAsAddedRecursive(entity, _options.MaxDepth);

    public void RecordSuccess(TEntity entity, int index, MixedKeyBatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);

        _insertedEntities.Add(new MixedKeyInsertedEntity
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });

        var (node, stats) = context.BuildMixedKeyGraphHierarchy(entity, _options.MaxDepth);
        _graphHierarchy.Add(node);
        AggregateStats(stats);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, MixedKeyBatchStrategyContext<TEntity> context)
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

    public void CleanupEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context) =>
        context.DetachEntityGraphRecursive(entity, _options.MaxDepth);

    public MixedKeyInsertBatchResult CreateResult() => new()
    {
        InsertedEntities = _insertedEntities,
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
