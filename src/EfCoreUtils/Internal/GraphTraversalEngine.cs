using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal;

/// <summary>
/// Options for configuring graph traversal behavior.
/// </summary>
internal sealed class TraversalOptions
{
    /// <summary>
    /// When true, process children before parent (for deletes).
    /// When false, process parent before children (for inserts/updates).
    /// Default: false (top-down).
    /// </summary>
    internal bool BottomUp { get; init; } = false;

    /// <summary>
    /// When true, include reference navigations in traversal.
    /// Default: false.
    /// </summary>
    internal bool IncludeReferences { get; init; } = false;

    /// <summary>
    /// When true, skip many-to-many navigations during child traversal.
    /// Default: true.
    /// </summary>
    internal bool SkipManyToMany { get; init; } = true;

    /// <summary>
    /// Default traversal options (top-down, no references, skip many-to-many).
    /// </summary>
    internal static TraversalOptions Default { get; } = new();

    /// <summary>
    /// Bottom-up traversal options (for deletes - children before parent).
    /// </summary>
    internal static TraversalOptions BottomUpDefault { get; } = new() { BottomUp = true };
}

/// <summary>
/// Engine for traversing entity graphs with configurable visitor behavior.
/// Handles cycle detection, depth tracking, and traversal order.
/// </summary>
internal sealed class GraphTraversalEngine
{
    private readonly DbContext _context;

    internal GraphTraversalEngine(DbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Traverses an entity graph invoking the visitor at each node.
    /// </summary>
    /// <typeparam name="TContext">Type of context passed to visitor.</typeparam>
    /// <param name="root">The root entity to start traversal from.</param>
    /// <param name="maxDepth">Maximum depth to traverse.</param>
    /// <param name="visitor">The visitor to invoke at each node.</param>
    /// <param name="visitorContext">Context passed to visitor methods.</param>
    /// <param name="options">Traversal options (null for default).</param>
    internal void Traverse<TContext>(
        object root,
        int maxDepth,
        IGraphVisitor<TContext> visitor,
        TContext visitorContext,
        TraversalOptions? options = null)
    {
        options ??= TraversalOptions.Default;
        var ctx = GraphTraversalContext.Create(maxDepth);

        if (options.BottomUp)
        {
            TraverseBottomUp(root, 0, ctx, visitor, visitorContext, options);
        }
        else
        {
            TraverseTopDown(root, 0, ctx, visitor, visitorContext, options);
        }
    }

    private void TraverseTopDown<TContext>(
        object entity, int depth, GraphTraversalContext ctx,
        IGraphVisitor<TContext> visitor, TContext visitorContext, TraversalOptions options)
    {
        var entityEntry = _context.Entry(entity);

        if (!ctx.TryVisit(entity))
        {
            visitor.OnCycleDetected(entityEntry, depth, visitorContext);
            return;
        }

        var shouldContinue = visitor.OnEnter(entityEntry, depth, visitorContext);

        if (shouldContinue && !ctx.IsAtMaxDepth(depth))
        {
            TraverseChildren(entityEntry, depth, ctx, visitor, visitorContext, options);
        }

        visitor.OnExit(entityEntry, depth, visitorContext);
    }

    private void TraverseBottomUp<TContext>(
        object entity, int depth, GraphTraversalContext ctx,
        IGraphVisitor<TContext> visitor, TContext visitorContext, TraversalOptions options)
    {
        var entityEntry = _context.Entry(entity);

        if (!ctx.TryVisit(entity))
        {
            visitor.OnCycleDetected(entityEntry, depth, visitorContext);
            return;
        }

        // Bottom-up: process children first
        if (!ctx.IsAtMaxDepth(depth))
        {
            TraverseChildrenBottomUp(entityEntry, depth, ctx, visitor, visitorContext, options);
        }

        // Then process parent
        var shouldProcess = visitor.OnEnter(entityEntry, depth, visitorContext);
        if (shouldProcess)
        {
            visitor.OnExit(entityEntry, depth, visitorContext);
        }
    }

    private void TraverseChildren<TContext>(
        EntityEntry entry, int depth, GraphTraversalContext ctx,
        IGraphVisitor<TContext> visitor, TContext visitorContext, TraversalOptions options)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!ShouldTraverseNavigation(navigation, options))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                TraverseTopDown(item, depth + 1, ctx, visitor, visitorContext, options);
            }
        }

        if (options.IncludeReferences)
        {
            TraverseReferences(entry, depth, ctx, visitor, visitorContext, options);
        }
    }

    private void TraverseChildrenBottomUp<TContext>(
        EntityEntry entry, int depth, GraphTraversalContext ctx,
        IGraphVisitor<TContext> visitor, TContext visitorContext, TraversalOptions options)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!ShouldTraverseNavigation(navigation, options))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                TraverseBottomUp(item, depth + 1, ctx, visitor, visitorContext, options);
            }
        }
    }

    private void TraverseReferences<TContext>(
        EntityEntry entry, int depth, GraphTraversalContext ctx,
        IGraphVisitor<TContext> visitor, TContext visitorContext, TraversalOptions options)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity != null)
            {
                TraverseTopDown(refEntity, depth + 1, ctx, visitor, visitorContext, options);
            }
        }
    }

    private static bool ShouldTraverseNavigation(NavigationEntry navigation, TraversalOptions options)
    {
        if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
        {
            return false;
        }

        if (options.SkipManyToMany && ManyToManyNavigationHelper.IsManyToManyNavigation(navigation))
        {
            return false;
        }

        return true;
    }
}
