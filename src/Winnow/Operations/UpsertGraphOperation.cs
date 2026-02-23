using Winnow.Internal;

namespace Winnow.Operations;

/// <summary>
/// Upsert operation behavior for entity graphs (parent + children).
/// Routes each entity to INSERT or UPDATE based on key value detection.
/// </summary>
/// <remarks>
/// <para><strong>Race Condition Warning:</strong></para>
/// <para>
/// This operation has a potential race condition between key detection and SaveChanges.
/// If another process inserts a row with the same key between these steps, the operation
/// may fail with a conflict error (classified as <see cref="FailureReason.DuplicateKey"/>).
/// </para>
/// <para><strong>Mitigation strategies:</strong></para>
/// <list type="bullet">
/// <item>Implement retry logic for failed operations</item>
/// <item>Use database-level MERGE/INSERT ON CONFLICT for high-concurrency scenarios</item>
/// <item>Add optimistic concurrency tokens to detect conflicts</item>
/// </list>
/// </remarks>
internal class UpsertGraphOperation<TEntity, TKey> : IUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertGraphOptions _options;
    private readonly TraversalContext _tc;
    private readonly List<UpsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<UpsertedEntity<TKey>> _updatedEntities = [];
    private readonly List<UpsertFailure<TKey>> _failures = [];
    private readonly List<GraphNode<TKey>> _graphHierarchy = [];
    private readonly Dictionary<int, UpsertOperationType> _operationDecisions = [];
    private readonly GraphStatisticsTracker<TKey> _statsTracker = new();

    internal UpsertGraphOperation(UpsertGraphOptions options)
    {
        _options = options;
        _tc = TraversalContext.FromOptions(options);
    }

    public void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        NavigationFilterValidator.Validate(
            _tc.NavigationFilter, context.Context.Model, _options.IncludeReferences, _options.IncludeManyToMany);

        context.CaptureAllOriginalChildIdsRecursive(entities, _tc);

        if (_options.IncludeManyToMany)
        {
            context.CaptureOriginalManyToManyLinks(entities, _tc);
            context.ValidateManyToManyEntitiesExistBatched(entities, ToInsertGraphOptions());
        }

        foreach (var entity in entities)
        {
            if (!context.HasDefaultKeyValue(entity))
            {
                context.ValidateNoOrphanedChildrenRecursive(entity, _tc, ToGraphOptions());
            }

            if (_options.IncludeReferences)
            {
                context.ValidateCircularReferences(entity, _tc);
            }
        }
    }

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var isInsert = context.HasDefaultKeyValue(entity);
        _operationDecisions[index] = isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update;

        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsUpsertWithReferences(entity, _tc);
            _statsTracker.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsUpsertRecursive(entity, _tc);
        }

        if (!isInsert)
        {
            context.HandleOrphanedChildrenRecursive(entity, _tc, _options.OrphanedChildBehavior);
        }

        if (_options.IncludeManyToMany)
        {
            ProcessManyToMany(entity, isInsert, context);
        }
    }

    private void ProcessManyToMany(TEntity entity, bool isInsert, StrategyContext<TEntity, TKey> context)
    {
        if (isInsert)
        {
            var m2mResult = context.ProcessManyToManyForInsert(entity, ToInsertGraphOptions());
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }
        else
        {
            var m2mResult = context.ApplyManyToManyChanges(entity, ToGraphOptions());
            _statsTracker.AggregateManyToManyStats(m2mResult);
        }
    }

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
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
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _graphHierarchy.Add(node);
        _statsTracker.AggregateStats(stats);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);
        TKey? entityId = default;

        if (operation == UpsertOperationType.Update)
        {
            entityId = context.GetEntityId(entity);
        }

        var failure = new UpsertFailure<TKey>
        {
            EntityIndex = index,
            EntityId = entityId,
            ErrorMessage = $"Graph upsert ({operation}) failed: {ex.Message}",
            Reason = FailureClassifier.Classify(ex),
            Exception = ex,
            AttemptedOperation = operation,
            IsDefaultKey = operation == UpsertOperationType.Insert
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.DetachEntityWithOrphansRecursive(entity, _tc);

    public UpsertResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        InsertedEntities = _insertedEntities,
        UpdatedEntities = _updatedEntities,
        Failures = _failures,
        GraphHierarchy = _graphHierarchy,
        TraversalInfo = _statsTracker.CreateTraversalInfo(),
        WasCancelled = wasCancelled
    };

    private GraphOptions ToGraphOptions() => new()
    {
        MaxDepth = _options.MaxDepth,
        OrphanedChildBehavior = _options.OrphanedChildBehavior,
        IncludeReferences = _options.IncludeReferences,
        CircularReferenceHandling = _options.CircularReferenceHandling,
        IncludeManyToMany = _options.IncludeManyToMany,
        NavigationFilter = _options.NavigationFilter
    };

    private InsertGraphOptions ToInsertGraphOptions() => new()
    {
        MaxDepth = _options.MaxDepth,
        IncludeReferences = _options.IncludeReferences,
        CircularReferenceHandling = _options.CircularReferenceHandling,
        IncludeManyToMany = _options.IncludeManyToMany,
        ManyToManyInsertBehavior = _options.ManyToManyInsertBehavior,
        ValidateManyToManyEntitiesExist = _options.ValidateManyToManyEntitiesExist,
        MaxManyToManyCollectionSize = _options.MaxManyToManyCollectionSize,
        ThrowOnUnsupportedValidation = _options.ThrowOnUnsupportedValidation,
        NavigationFilter = _options.NavigationFilter
    };

    public bool WasInsertAttempt(int index) =>
        _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert) == UpsertOperationType.Insert;

    public DuplicateKeyStrategy DuplicateKeyStrategy => _options.DuplicateKeyStrategy;

    public void RecordSuccessAsUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _operationDecisions[index] = UpsertOperationType.Update;

        var upsertedEntity = new UpsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity,
            Operation = UpsertOperationType.Update
        };
        _updatedEntities.Add(upsertedEntity);

        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _graphHierarchy.Add(node);
        _statsTracker.AggregateStats(stats);
    }
}
