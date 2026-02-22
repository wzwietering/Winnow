using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal.Visitors;

/// <summary>
/// Context for conditional state visitor operations.
/// </summary>
internal sealed class StateVisitorContext
{
    internal EntityState TargetState { get; init; }
    internal bool OnlyIfDetached { get; init; }
}

/// <summary>
/// Visitor that conditionally sets entity state during graph traversal.
/// Supports setting state only when entity is currently detached (for Modified operations).
/// </summary>
internal sealed class ConditionalStateVisitor : IGraphVisitor<StateVisitorContext>
{
    public bool OnEnter(EntityEntry entry, int depth, StateVisitorContext ctx)
    {
        if (ctx.OnlyIfDetached && entry.State != EntityState.Detached)
        {
            return true;
        }

        entry.State = ctx.TargetState;
        return true;
    }

    public void OnExit(EntityEntry entry, int depth, StateVisitorContext ctx) { }

    public void OnCycleDetected(EntityEntry entry, int depth, StateVisitorContext ctx) { }
}
