using Winnow.Internal;
using Winnow.Internal.Accumulators;

namespace Winnow.Operations;

/// <summary>
/// Delete operation behavior for entity graphs (parent + children).
/// Handles cascade behavior and child tracking.
/// </summary>
internal class DeleteGraphOperation<TEntity, TKey> : IOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DeleteGraphOptions _options;
    private readonly TraversalContext _tc;
    private readonly WinnowAccumulator<TKey> _accumulator;
    private readonly GraphResultAccumulator<TKey> _graph;
    private readonly Dictionary<TKey, (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats)> _pendingGraphNodes = [];

    internal DeleteGraphOperation(
        DeleteGraphOptions options,
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

        foreach (var entity in entities)
        {
            if (_options.CascadeBehavior == DeleteCascadeBehavior.Throw)
            {
                context.ValidateCascadeBehaviorRecursive(entity, _tc, _options);
            }

            if (_options.ValidateReferencedEntitiesExist)
            {
                context.ValidateReferencedEntitiesExist(entity, _tc);
            }
        }
    }

    public void PrepareEntity(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        if (_graph.IsActive)
        {
            // CRITICAL: build graph hierarchy BEFORE marking as deleted
            var entityId = context.GetEntityId(entity);
            var (node, stats) = context.BuildGraphHierarchy(entity, _tc);
            _pendingGraphNodes[entityId] = (node, stats);
        }

        if (_options.IncludeManyToMany)
        {
            var m2mResult = context.ProcessManyToManyForDelete(entity);
            _graph.AggregateManyToManyStats(m2mResult);
        }

        if (_options.CascadeBehavior == DeleteCascadeBehavior.ParentOnly)
        {
            context.AttachEntityAsDeleted(entity);
        }
        else
        {
            context.AttachEntityGraphAsDeletedRecursive(entity, _tc);
        }
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
            $"Graph delete failed: {ex.Message}",
            FailureClassifier.Classify(ex),
            ex);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context) =>
        context.DetachEntityGraphRecursive(entity, _tc);

    public WinnowResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled, _graph);
}
