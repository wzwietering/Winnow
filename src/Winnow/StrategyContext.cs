using Winnow.Internal;
using Winnow.Internal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow;

internal class StrategyContext<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    internal const int AbsoluteMaxDepth = DepthConstants.AbsoluteMaxDepth;

    private readonly DbContext _context;
    private int _roundTripCounter;

    // Services
    private readonly EntityKeyService<TEntity, TKey> _keyService;
    private readonly ValidationService<TEntity, TKey> _validationService;
    private readonly EntityAttachmentService<TEntity, TKey> _attachmentService;
    private readonly EntityDetachmentService<TEntity, TKey> _detachmentService;
    private readonly OrphanTrackingService<TEntity, TKey> _orphanService;
    private readonly LinkChangeTrackingService<TEntity, TKey> _linkChangeService;

    // Many-to-many services
    private readonly ManyToManyIdQueryService _m2mIdQueryService;
    private readonly ManyToManyValidationCache<TEntity, TKey> _m2mValidationCache;
    private readonly ManyToManyInsertProcessor<TEntity, TKey> _m2mInsertProcessor;
    private readonly ManyToManyDeleteProcessor<TEntity, TKey> _m2mDeleteProcessor;

    private GraphHierarchyBuilder<TKey>? _graphBuilder;

    internal StrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;

        _keyService = new EntityKeyService<TEntity, TKey>(context);
        _validationService = new ValidationService<TEntity, TKey>(context, _keyService);
        _attachmentService = new EntityAttachmentService<TEntity, TKey>(context);
        _detachmentService = new EntityDetachmentService<TEntity, TKey>(context);
        _orphanService = new OrphanTrackingService<TEntity, TKey>(context, _keyService);
        _linkChangeService = new LinkChangeTrackingService<TEntity, TKey>(context, _keyService);

        // Many-to-many services with proper dependency chain
        _m2mIdQueryService = new ManyToManyIdQueryService(context);
        _m2mValidationCache = new ManyToManyValidationCache<TEntity, TKey>(context, _m2mIdQueryService);
        _m2mInsertProcessor = new ManyToManyInsertProcessor<TEntity, TKey>(context, _m2mValidationCache);
        _m2mDeleteProcessor = new ManyToManyDeleteProcessor<TEntity, TKey>(context);
    }

    private GraphHierarchyBuilder<TKey> GraphBuilder =>
        _graphBuilder ??= new GraphHierarchyBuilder<TKey>(_context, _keyService.GetEntityIdFromEntry);

    internal DbContext Context => _context;
    internal ILogger? Logger { get; init; }
    internal RetryOptions? RetryOptions { get; init; }
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

    private int _retryCounter;
    internal int RetryCounter => _retryCounter;
    internal void IncrementRetryCount() => _retryCounter++;

    // ========== Key Service Delegation ==========

    internal TKey GetEntityId(TEntity entity) => _keyService.GetEntityId(entity);

    internal string GetEntityIdString(TEntity entity)
    {
        try { return GetEntityId(entity).ToString()!; }
        catch (Exception) { return "unknown"; }
    }

    // ========== Batch Result Factory Delegation ==========

    internal WinnowFailure<TKey> CreateWinnowFailure(TKey entityId, Exception exception) =>
        ResultFactory.CreateWinnowFailure(entityId, exception);

    internal InsertFailure CreateInsertFailure(int entityIndex, Exception exception) =>
        ResultFactory.CreateInsertFailure(entityIndex, exception);

    // ========== Validation Service Delegation ==========

    internal void ValidateNoModifiedNavigationProperties(TEntity entity) =>
        _validationService.ValidateNoModifiedNavigationProperties(entity);

    internal void ValidateNoPopulatedNavigationProperties(TEntity entity) =>
        _validationService.ValidateNoPopulatedNavigationProperties(entity);

    internal void ValidateNoPopulatedNavigationPropertiesForDelete(TEntity entity) =>
        _validationService.ValidateNoPopulatedNavigationPropertiesForDelete(entity);

    internal void ValidateCascadeBehavior(TEntity entity, DeleteGraphOptions options) =>
        _validationService.ValidateCascadeBehavior(entity, options);

    internal void ValidateCascadeBehaviorRecursive(
        TEntity entity, TraversalContext tc, DeleteGraphOptions options) =>
        _validationService.ValidateCascadeBehaviorRecursive(entity, tc, options);

    internal void ValidateCircularReferences(TEntity entity, TraversalContext tc) =>
        _validationService.ValidateCircularReferences(entity, tc);

    internal void ValidateReferencedEntitiesExist(TEntity entity, TraversalContext tc) =>
        _validationService.ValidateReferencedEntitiesExist(entity, tc);

    internal bool HasDefaultKeyValue(TEntity entity) =>
        _validationService.HasDefaultKeyValue(entity);

    // ========== Attachment Service Delegation ==========

    internal void AttachEntityAsDeleted(TEntity entity) =>
        _attachmentService.AttachEntityAsDeleted(entity);

    internal void AttachEntityGraphAsAdded(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsAdded(entity);

    internal void AttachEntityGraphAsModified(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsModified(entity);

    internal void AttachEntityGraphAsDeleted(TEntity entity) =>
        _attachmentService.AttachEntityGraphAsDeleted(entity);

    internal void AttachEntityGraphAsAddedRecursive(TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsAddedRecursive(entity, tc);

    internal void AttachEntityGraphAsModifiedRecursive(TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsModifiedRecursive(entity, tc);

    internal void AttachEntityGraphAsDeletedRecursive(TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsDeletedRecursive(entity, tc);

    internal ReferenceTrackingResult AttachEntityGraphAsAddedWithReferences(
        TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsAddedWithReferences(entity, tc);

    internal ReferenceTrackingResult AttachEntityGraphAsModifiedWithReferences(
        TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsModifiedWithReferences(entity, tc);

    internal void AttachEntityGraphAsUpsertRecursive(TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsUpsertRecursive(entity, tc, _validationService);

    internal ReferenceTrackingResult AttachEntityGraphAsUpsertWithReferences(
        TEntity entity, TraversalContext tc) =>
        _attachmentService.AttachEntityGraphAsUpsertWithReferences(
            entity, tc, _validationService);

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

    internal void DetachEntityGraphRecursive(TEntity entity, TraversalContext tc) =>
        _detachmentService.DetachEntityGraphRecursive(entity, tc);

    // ========== Orphan Service Delegation ==========

    internal void CaptureAllOriginalChildIds(List<TEntity> entities) =>
        _orphanService.CaptureAllOriginalChildIds(entities);

    internal List<TKey> GetChildIds(TEntity entity) =>
        _orphanService.GetChildIds(entity);

    internal List<TKey> GetOrphanedChildIds(TEntity entity) =>
        _orphanService.GetOrphanedChildIds(entity);

    internal void ValidateNoOrphanedChildren(TEntity entity, GraphOptions options) =>
        _orphanService.ValidateNoOrphanedChildren(entity, options);

    internal void HandleOrphanedChildren(TEntity entity, GraphOptions options) =>
        _orphanService.HandleOrphanedChildren(entity, options);

    internal void DetachEntityWithOrphans(TEntity entity) =>
        _orphanService.DetachEntityWithOrphans(entity, _detachmentService);

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, TraversalContext tc) =>
        _orphanService.CaptureAllOriginalChildIdsRecursive(entities, tc);

    internal void ValidateNoOrphanedChildrenRecursive(
        TEntity entity, TraversalContext tc, GraphOptions options) =>
        _orphanService.ValidateNoOrphanedChildrenRecursive(entity, tc, options);

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, TraversalContext tc, OrphanBehavior behavior) =>
        _orphanService.HandleOrphanedChildrenRecursive(entity, tc, behavior);

    internal void DetachEntityWithOrphansRecursive(TEntity entity, TraversalContext tc) =>
        _orphanService.DetachEntityWithOrphansRecursive(entity, tc, _detachmentService);

    // ========== Graph Hierarchy ==========

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildGraphHierarchy(
        TEntity entity, TraversalContext tc) => GraphBuilder.Build(entity, tc);

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildGraphHierarchyWithReferences(
        TEntity entity, TraversalContext tc) => GraphBuilder.BuildWithReferences(entity, tc);

    // ========== Many-to-Many Service Delegation ==========

    internal Internal.ManyToManyStatisticsTracker ProcessManyToManyForInsert(
        TEntity entity, InsertGraphOptions options) =>
        _m2mInsertProcessor.ProcessManyToManyForInsert(entity, options);

    internal Internal.ManyToManyStatisticsTracker ProcessManyToManyForDelete(TEntity entity) =>
        _m2mDeleteProcessor.ProcessManyToManyForDelete(entity);

    internal void ValidateManyToManyEntitiesExistBatched(
        List<TEntity> entities, InsertGraphOptions options) =>
        _m2mValidationCache.ValidateManyToManyEntitiesExistBatched(entities, options);

    // ========== Link Change Tracking Service Delegation ==========

    internal void CaptureOriginalManyToManyLinks(List<TEntity> entities, TraversalContext tc) =>
        _linkChangeService.CaptureOriginalLinks(entities, tc);

    internal Internal.ManyToManyStatisticsTracker ApplyManyToManyChanges(TEntity entity, GraphOptions options) =>
        _linkChangeService.ApplyLinkChanges(entity, options);
}
