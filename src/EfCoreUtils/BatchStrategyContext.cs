using EfCoreUtils.Internal;
using EfCoreUtils.Internal.Services;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

internal class BatchStrategyContext<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    internal const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;
    private int _roundTripCounter;

    // Services
    private readonly EntityKeyService<TEntity, TKey> _keyService;
    private readonly ValidationService<TEntity, TKey> _validationService;
    private readonly EntityAttachmentService<TEntity, TKey> _attachmentService;
    private readonly EntityDetachmentService<TEntity, TKey> _detachmentService;
    private readonly OrphanTrackingService<TEntity, TKey> _orphanService;

    private GraphHierarchyBuilder<TKey>? _graphBuilder;

    internal BatchStrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;

        _keyService = new EntityKeyService<TEntity, TKey>(context);
        _validationService = new ValidationService<TEntity, TKey>(context, _keyService);
        _attachmentService = new EntityAttachmentService<TEntity, TKey>(context);
        _detachmentService = new EntityDetachmentService<TEntity, TKey>(context);
        _orphanService = new OrphanTrackingService<TEntity, TKey>(context, _keyService);
    }

    private GraphHierarchyBuilder<TKey> GraphBuilder =>
        _graphBuilder ??= new GraphHierarchyBuilder<TKey>(_context, _keyService.GetEntityIdFromEntry);

    internal DbContext Context => _context;
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

    // ========== Key Service Delegation ==========

    internal TKey GetEntityId(TEntity entity) => _keyService.GetEntityId(entity);

    // ========== Batch Result Factory Delegation ==========

    internal BatchFailure<TKey> CreateBatchFailure(TKey entityId, Exception exception) =>
        BatchResultFactory.CreateBatchFailure(entityId, exception);

    internal InsertBatchFailure CreateInsertBatchFailure(int entityIndex, Exception exception) =>
        BatchResultFactory.CreateInsertBatchFailure(entityIndex, exception);

    // ========== Validation Service Delegation ==========

    internal void ValidateNoModifiedNavigationProperties(TEntity entity) =>
        _validationService.ValidateNoModifiedNavigationProperties(entity);

    internal void ValidateNoPopulatedNavigationProperties(TEntity entity) =>
        _validationService.ValidateNoPopulatedNavigationProperties(entity);

    internal void ValidateNoPopulatedNavigationPropertiesForDelete(TEntity entity) =>
        _validationService.ValidateNoPopulatedNavigationPropertiesForDelete(entity);

    internal void ValidateCascadeBehavior(TEntity entity, DeleteGraphBatchOptions options) =>
        _validationService.ValidateCascadeBehavior(entity, options);

    internal void ValidateCascadeBehaviorRecursive(
        TEntity entity, int maxDepth, DeleteGraphBatchOptions options) =>
        _validationService.ValidateCascadeBehaviorRecursive(entity, maxDepth, options);

    // ========== Attachment Service Delegation ==========

    internal void AttachEntityAsDeleted(TEntity entity) =>
        _attachmentService.AttachEntityAsDeleted(entity);

    internal void AttachEntityGraphAsAdded(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsAdded(entity);

    internal void AttachEntityGraphAsModified(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsModified(entity);

    internal void AttachEntityGraphAsDeleted(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsDeleted(entity);

    internal void AttachEntityGraphAsAddedRecursive(TEntity entity, int maxDepth) =>
        _attachmentService.AttachEntityGraphAsAddedRecursive(entity, maxDepth);

    internal void AttachEntityGraphAsModifiedRecursive(TEntity entity, int maxDepth) =>
        _attachmentService.AttachEntityGraphAsModifiedRecursive(entity, maxDepth);

    internal void AttachEntityGraphAsDeletedRecursive(TEntity entity, int maxDepth) =>
        _attachmentService.AttachEntityGraphAsDeletedRecursive(entity, maxDepth);

    // ========== Detachment Service Delegation ==========

    internal void DetachEntity(TEntity entity) =>
        _detachmentService.DetachEntity(entity);

    internal void DetachEntityGraph(TEntity entity) =>
        _detachmentService.DetachEntityGraph(entity);

    internal void DetachAllEntities(List<TEntity> entities) =>
        _detachmentService.DetachAllEntities(
            entities,
            _orphanService.DeletedChildrenByParent,
            _orphanService.DeletedChildrenByParentRecursive);

    internal void DetachEntityGraphRecursive(TEntity entity, int maxDepth) =>
        _detachmentService.DetachEntityGraphRecursive(entity, maxDepth);

    // ========== Orphan Service Delegation ==========

    internal void CaptureAllOriginalChildIds(List<TEntity> entities) =>
        _orphanService.CaptureAllOriginalChildIds(entities);

    internal List<TKey> GetChildIds(TEntity entity) =>
        _orphanService.GetChildIds(entity);

    internal List<TKey> GetOrphanedChildIds(TEntity entity) =>
        _orphanService.GetOrphanedChildIds(entity);

    internal void ValidateNoOrphanedChildren(TEntity entity, GraphBatchOptions options) =>
        _orphanService.ValidateNoOrphanedChildren(entity, options);

    internal void HandleOrphanedChildren(TEntity entity, GraphBatchOptions options) =>
        _orphanService.HandleOrphanedChildren(entity, options);

    internal void DetachEntityWithOrphans(TEntity entity) =>
        _orphanService.DetachEntityWithOrphans(entity, _detachmentService);

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, int maxDepth) =>
        _orphanService.CaptureAllOriginalChildIdsRecursive(entities, maxDepth);

    internal List<(string EntityType, TKey EntityId, int Depth)> GetOrphanedChildIdsRecursive(
        TEntity entity, int maxDepth) =>
        _orphanService.GetOrphanedChildIdsRecursive(entity, maxDepth);

    internal void ValidateNoOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, GraphBatchOptions options) =>
        _orphanService.ValidateNoOrphanedChildrenRecursive(entity, maxDepth, options);

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, OrphanBehavior behavior) =>
        _orphanService.HandleOrphanedChildrenRecursive(entity, maxDepth, behavior);

    internal void DetachEntityWithOrphansRecursive(TEntity entity, int maxDepth) =>
        _orphanService.DetachEntityWithOrphansRecursive(entity, maxDepth, _detachmentService);

    // ========== Graph Hierarchy ==========

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildGraphHierarchy(
        TEntity entity, int maxDepth) => GraphBuilder.Build(entity, maxDepth);
}
