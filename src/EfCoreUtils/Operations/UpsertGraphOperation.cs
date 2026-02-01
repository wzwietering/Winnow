using EfCoreUtils.Internal;

namespace EfCoreUtils.Operations;

/// <summary>
/// Upsert operation behavior for entity graphs (parent + children).
/// Routes each entity to INSERT or UPDATE based on key value detection.
/// </summary>
/// <remarks>
/// <para><strong>Race Condition Warning:</strong></para>
/// <para>
/// This operation has a potential race condition between key detection and SaveChanges.
/// If another process inserts a row with the same key between these steps, the operation
/// may fail with a conflict error (classified as <see cref="FailureReason.Conflict"/>).
/// </para>
/// <para><strong>Mitigation strategies:</strong></para>
/// <list type="bullet">
/// <item>Implement retry logic for failed operations</item>
/// <item>Use database-level MERGE/INSERT ON CONFLICT for high-concurrency scenarios</item>
/// <item>Add optimistic concurrency tokens to detect conflicts</item>
/// </list>
/// </remarks>
internal class UpsertGraphOperation<TEntity, TKey> : IBatchUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertGraphBatchOptions _options;
    private readonly List<UpsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<UpsertedEntity<TKey>> _updatedEntities = [];
    private readonly List<UpsertBatchFailure<TKey>> _failures = [];
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<int, UpsertOperationType> _operationDecisions = [];
    private readonly GraphStatisticsTracker<TKey> _statsTracker = new();

    internal UpsertGraphOperation(UpsertGraphBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        // Capture original child IDs for orphan detection (applies to update paths)
        context.CaptureAllOriginalChildIdsRecursive(entities, _options.MaxDepth);

        if (_options.IncludeManyToMany)
        {
            context.CaptureOriginalManyToManyLinks(entities, _options.MaxDepth);
        }

        foreach (var entity in entities)
        {
            // Only validate orphans for entities that will be updated
            if (!context.HasDefaultKeyValue(entity))
            {
                context.ValidateNoOrphanedChildrenRecursive(entity, _options.MaxDepth, ToGraphBatchOptions());
            }

            if (_options.IncludeReferences)
            {
                context.ValidateCircularReferences(
                    entity, _options.MaxDepth, _options.CircularReferenceHandling);
            }
        }
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var isInsert = context.HasDefaultKeyValue(entity);
        _operationDecisions[index] = isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update;

        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsUpsertWithReferences(
                entity, _options.MaxDepth, _options.CircularReferenceHandling);
            _statsTracker.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsUpsertRecursive(entity, _options.MaxDepth);
        }

        // Handle orphans only for update paths
        if (!isInsert)
        {
            context.HandleOrphanedChildrenRecursive(entity, _options.MaxDepth, _options.OrphanedChildBehavior);
        }

        if (_options.IncludeManyToMany)
        {
            ProcessManyToMany(entity, isInsert, context);
        }
    }

    private void ProcessManyToMany(TEntity entity, bool isInsert, BatchStrategyContext<TEntity, TKey> context)
    {
        if (isInsert)
        {
            var m2mResult = context.ProcessManyToManyForInsert(entity, ToInsertGraphOptions());
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }
        else
        {
            var m2mResult = context.ApplyManyToManyChanges(entity, ToGraphBatchOptions());
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);

        var upsertedEntity = new UpsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity,
            Operation = operation
        };

        if (operation == UpsertOperationType.Insert)
        {
            _insertedEntities.Add(upsertedEntity);
        }
        else
        {
            _updatedEntities.Add(upsertedEntity);
        }

        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _options.MaxDepth)
            : context.BuildGraphHierarchy(entity, _options.MaxDepth);
        _graphHierarchy.Add(node);
        _statsTracker.AggregateStats(stats);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);
        TKey? entityId = default;

        if (operation == UpsertOperationType.Update)
        {
            entityId = context.GetEntityId(entity);
        }

        var failure = new UpsertBatchFailure<TKey>
        {
            EntityIndex = index,
            EntityId = entityId,
            ErrorMessage = $"Graph upsert ({operation}) failed: {ex.Message}",
            Reason = FailureClassifier.Classify(ex),
            Exception = ex,
            AttemptedOperation = operation
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) =>
        context.DetachEntityWithOrphansRecursive(entity, _options.MaxDepth);

    public UpsertBatchResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        InsertedEntities = _insertedEntities,
        UpdatedEntities = _updatedEntities,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy.ToDictionary(
            n => n.EntityId,
            n => n),
        TraversalInfo = _statsTracker.CreateTraversalInfo(),
        WasCancelled = wasCancelled
    };

    private GraphBatchOptions ToGraphBatchOptions() => new()
    {
        MaxDepth = _options.MaxDepth,
        OrphanedChildBehavior = _options.OrphanedChildBehavior,
        IncludeReferences = _options.IncludeReferences,
        CircularReferenceHandling = _options.CircularReferenceHandling,
        IncludeManyToMany = _options.IncludeManyToMany
    };

    private InsertGraphBatchOptions ToInsertGraphOptions() => new()
    {
        MaxDepth = _options.MaxDepth,
        IncludeReferences = _options.IncludeReferences,
        CircularReferenceHandling = _options.CircularReferenceHandling,
        IncludeManyToMany = _options.IncludeManyToMany,
        ManyToManyInsertBehavior = _options.ManyToManyInsertBehavior
    };
}
