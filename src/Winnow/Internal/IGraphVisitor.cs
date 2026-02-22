using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal;

/// <summary>
/// Visitor interface for graph traversal operations.
/// Implement this interface to define custom behavior for each node during traversal.
/// </summary>
/// <typeparam name="TContext">Context type for passing state through traversal.</typeparam>
internal interface IGraphVisitor<TContext>
{
    /// <summary>
    /// Called when entering an entity during traversal.
    /// Return false to skip this entity's children.
    /// </summary>
    /// <param name="entry">The entity entry being visited.</param>
    /// <param name="depth">Current depth in the traversal.</param>
    /// <param name="context">User-provided context for state passing.</param>
    /// <returns>True to continue traversing children, false to skip children.</returns>
    bool OnEnter(EntityEntry entry, int depth, TContext context);

    /// <summary>
    /// Called after processing all children of an entity.
    /// Only called if OnEnter returned true.
    /// </summary>
    /// <param name="entry">The entity entry being visited.</param>
    /// <param name="depth">Current depth in the traversal.</param>
    /// <param name="context">User-provided context for state passing.</param>
    void OnExit(EntityEntry entry, int depth, TContext context);

    /// <summary>
    /// Called when a cycle is detected (entity already visited).
    /// </summary>
    /// <param name="entry">The entity entry that was already visited.</param>
    /// <param name="depth">Current depth in the traversal.</param>
    /// <param name="context">User-provided context for state passing.</param>
    void OnCycleDetected(EntityEntry entry, int depth, TContext context);
}
