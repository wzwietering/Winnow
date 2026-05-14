using Winnow.Internal;
using Winnow.Internal.Accumulators;

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
    private readonly UpsertAccumulator<TKey> _accumulator;
    private readonly GraphResultAccumulator<TKey> _graph;

    internal UpsertGraphOperation(
        UpsertGraphOptions options,
        UpsertAccumulator<TKey> accumulator,
        GraphResultAccumulator<TKey> graph)
    {
        _options = options;
        _accumulator = accumulator;
        _graph = graph;
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
        _accumulator.RecordOperationDecision(
            index, isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update);

        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsUpsertWithReferences(entity, _tc);
            _graph.AggregateReferenceStats(refResult);
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
            _graph.AggregateManyToManyStats(m2mResult);
        }
        else
        {
            var m2mResult = context.ApplyManyToManyChanges(entity, ToGraphOptions());
            _graph.AggregateManyToManyStats(m2mResult);
        }
    }

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        _accumulator.RecordSuccess(context.GetEntityId(entity), index, entity);
        AddHierarchyForEntity(entity, context);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var operation = _accumulator.GetOperationDecision(index);
        TKey? entityId = operation == UpsertOperationType.Update ? context.GetEntityId(entity) : default;
        _accumulator.RecordFailure(
            index,
            entityId,
            $"Graph upsert ({operation}) failed: {ex.Message}",
            FailureClassifier.Classify(ex),
            ex,
            operation);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.DetachEntityWithOrphansRecursive(entity, _tc);

    public UpsertResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled, _graph);

    private GraphOptions ToGraphOptions() => new()
    {
        MaxDepth = _options.MaxDepth,
        OrphanedChildBehavior = _options.OrphanedChildBehavior,
        IncludeReferences = _options.IncludeReferences,
        CircularReferenceHandling = _options.CircularReferenceHandling,
        IncludeManyToMany = _options.IncludeManyToMany,
        NavigationFilter = _options.NavigationFilter,
        ResultDetail = _options.ResultDetail
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
        NavigationFilter = _options.NavigationFilter,
        ResultDetail = _options.ResultDetail
    };

    public bool WasInsertAttempt(int index) => _accumulator.WasInsertAttempt(index);

    public DuplicateKeyStrategy DuplicateKeyStrategy => _options.DuplicateKeyStrategy;

    public void RecordSuccessAsUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        _accumulator.RecordSuccessAsUpdate(context.GetEntityId(entity), index, entity);
        AddHierarchyForEntity(entity, context);
    }

    private void AddHierarchyForEntity(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        if (!_graph.IsActive)
        {
            return;
        }
        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _graph.AddHierarchyNode(node, stats);
    }
}
