using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal.Visitors;

/// <summary>
/// Visitor that sets the entity state during graph traversal.
/// Used for simple state assignment (delete, detach) operations.
/// </summary>
internal sealed class EntityStateVisitor : IGraphVisitor<EntityState>
{
    public bool OnEnter(EntityEntry entry, int depth, EntityState targetState)
    {
        entry.State = targetState;
        return true;
    }

    public void OnExit(EntityEntry entry, int depth, EntityState targetState) { }

    public void OnCycleDetected(EntityEntry entry, int depth, EntityState targetState) { }
}
