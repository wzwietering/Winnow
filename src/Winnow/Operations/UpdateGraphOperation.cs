using Winnow.Internal;
using Winnow.Internal.Accumulators;

namespace Winnow.Operations;

/// <summary>
/// Update operation behavior for entity graphs (parent + children).
/// Handles orphan detection, child tracking, and graph attachment.
/// </summary>
internal class UpdateGraphOperation<TEntity, TKey> : IOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly GraphOptions _options;
    private readonly TraversalContext _tc;
    private readonly WinnowAccumulator<TKey> _accumulator;
    private readonly GraphResultAccumulator<TKey> _graph;
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];

    internal UpdateGraphOperation(
        GraphOptions options,
        WinnowAccumulator<TKey> accumulator,
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

    public void PrepareEntity(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        if (_options.IncludeReferences)
        {
            var refResult = context.AttachEntityGraphAsModifiedWithReferences(entity, _tc);
            _graph.AggregateReferenceStats(refResult);
        }
        else
        {
            context.AttachEntityGraphAsModifiedRecursive(entity, _tc);
        }

        context.HandleOrphanedChildrenRecursive(entity, _tc, _options.OrphanedChildBehavior);

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ApplyManyToManyChanges(entity, _options);
            _graph.AggregateManyToManyStats(m2mResult);
        }

        if (!_graph.IsActive)
        {
            return;
        }
        var entityId = context.GetEntityId(entity);
        var (node, stats) = _options.IncludeReferences
            ? context.BuildGraphHierarchyWithReferences(entity, _tc)
            : context.BuildGraphHierarchy(entity, _tc);
        _pendingGraphNodes[entityId] = (node, stats);
    }

    public void RecordSuccess(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _accumulator.RecordSuccess(entityId);

        if (_pendingGraphNodes.Remove(entityId, out var pending))
        {
            _graph.AddHierarchyNode(pending.Node, pending.Stats);
        }
    }

    public void RecordFailure(TEntity entity, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _pendingGraphNodes.Remove(entityId);
        _accumulator.RecordFailure(
            entityId,
            $"Graph update failed: {ex.Message}",
            FailureClassifier.Classify(ex),
            ex);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.DetachEntityWithOrphansRecursive(entity, _tc);

    public WinnowResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled, _graph);
}
