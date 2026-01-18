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

    // Stats aggregation
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];

    // Reference tracking
    private readonly Dictionary<string, List<TKey>> _processedReferencesByType = [];
    private int _maxReferenceDepthReached;

    internal InsertGraphOperation(InsertGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        if (!_options.IncludeReferences ||
            _options.CircularReferenceHandling != CircularReferenceHandling.Throw)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateCircularReferences(entity, _options.MaxDepth);
        }
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsAddedWithReferences(
                entity, _options.MaxDepth, _options.CircularReferenceHandling);
            AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsAddedRecursive(entity, _options.MaxDepth);
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
        AggregateStats(stats);
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

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) => context.DetachEntityGraphRecursive(entity, _options.MaxDepth);

    public InsertBatchResult<TKey> CreateResult() => new()
    {
        InsertedEntities = _insertedEntities,
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
