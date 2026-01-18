using EfCoreUtils.Internal.MixedKey;
using EfCoreUtils.Internal.Services.MixedKey;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.MixedKey;

/// <summary>
/// Context for batch operations on entities with mixed key types.
/// Composes all mixed-key services needed for graph operations.
/// </summary>
internal class MixedKeyBatchStrategyContext<TEntity>
    where TEntity : class
{
    internal const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;
    private int _roundTripCounter;

    // Services
    private readonly MixedKeyEntityKeyService _keyService;
    private readonly MixedKeyValidationService<TEntity> _validationService;
    private readonly MixedKeyAttachmentService _attachmentService;
    private readonly MixedKeyDetachmentService _detachmentService;
    private readonly MixedKeyOrphanTrackingService<TEntity> _orphanService;

    private MixedKeyGraphHierarchyBuilder? _graphBuilder;

    internal MixedKeyBatchStrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;

        _keyService = new MixedKeyEntityKeyService(context);
        _validationService = new MixedKeyValidationService<TEntity>(context, _keyService);
        _attachmentService = new MixedKeyAttachmentService(context);
        _detachmentService = new MixedKeyDetachmentService(context);
        _orphanService = new MixedKeyOrphanTrackingService<TEntity>(context);
    }

    private MixedKeyGraphHierarchyBuilder GraphBuilder =>
        _graphBuilder ??= new MixedKeyGraphHierarchyBuilder(_context);

    internal DbContext Context => _context;
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

    // ========== Key Service Delegation ==========

    internal MixedKeyId GetEntityId(TEntity entity) => _keyService.GetEntityKey(entity);

    // ========== Batch Result Factory Delegation ==========

    internal MixedKeyBatchFailure CreateBatchFailure(MixedKeyId entityId, Exception exception) =>
        new()
        {
            EntityId = entityId,
            ErrorMessage = exception.Message,
            Reason = ClassifyException(exception),
            Exception = exception
        };

    internal InsertBatchFailure CreateInsertBatchFailure(int entityIndex, Exception exception) =>
        new()
        {
            EntityIndex = entityIndex,
            ErrorMessage = exception.Message,
            Reason = ClassifyException(exception),
            Exception = exception
        };

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
            _orphanService.DeletedChildrenByParentRecursive);

    internal void DetachEntityGraphRecursive(TEntity entity, int maxDepth) =>
        _detachmentService.DetachEntityGraphRecursive(entity, maxDepth);

    // ========== Orphan Service Delegation ==========

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, int maxDepth) =>
        _orphanService.CaptureAllOriginalChildIdsRecursive(entities, maxDepth);

    internal List<(string EntityType, object EntityId, int Depth)> GetOrphanedChildIdsRecursive(
        TEntity entity, int maxDepth) =>
        _orphanService.GetOrphanedChildIdsRecursive(entity, maxDepth);

    internal void ValidateNoOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, GraphBatchOptions options) =>
        _orphanService.ValidateNoOrphanedChildrenRecursive(entity, maxDepth, options);

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, OrphanBehavior behavior) =>
        _orphanService.HandleOrphanedChildrenRecursive(entity, maxDepth, behavior);

    internal void DetachEntityWithOrphansRecursive(TEntity entity, int maxDepth)
    {
        _orphanService.DetachAllDeletedChildrenRecursive();
        _detachmentService.DetachEntityGraphRecursive(entity, maxDepth);
    }

    // ========== Graph Hierarchy ==========

    internal (MixedKeyGraphNode Node, MixedKeyGraphTraversalResult Stats) BuildMixedKeyGraphHierarchy(
        TEntity entity, int maxDepth) => GraphBuilder.Build(entity, maxDepth);

    // ========== Helper Methods ==========

    private static FailureReason ClassifyException(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
