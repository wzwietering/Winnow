using Winnow.Internal;
using Winnow.Internal.Accumulators;

namespace Winnow.Operations;

internal class InsertGraphOperation<TEntity, TKey> : IInsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly InsertGraphOptions _options;
    private readonly TraversalContext _tc;
    private readonly InsertAccumulator<TKey> _accumulator;
    private readonly GraphResultAccumulator<TKey> _graph;

    internal InsertGraphOperation(
        InsertGraphOptions options,
        InsertAccumulator<TKey> accumulator,
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

        if (_options.IncludeReferences)
        {
            foreach (var entity in entities)
            {
                context.ValidateCircularReferences(entity, _tc);
            }
        }

        if (_options.IncludeManyToMany)
        {
            context.ValidateManyToManyEntitiesExistBatched(entities, _options);
        }
    }

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsAddedWithReferences(entity, _tc);
            _graph.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsAddedRecursive(entity, _tc);
        }

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ProcessManyToManyForInsert(entity, _options);
            _graph.AggregateManyToManyStats(m2mResult);
        }
    }

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _accumulator.RecordSuccess(entityId, index, entity);

        if (!_graph.IsActive)
        {
            return;
        }
        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _graph.AddHierarchyNode(node, stats);
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordFailure(
            index,
            $"Graph insert failed: {ex.Message}",
            FailureClassifier.Classify(ex),
            ex);

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.DetachEntityGraphRecursive(entity, _tc);

    public InsertResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled, _graph);
}
