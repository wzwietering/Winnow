using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

internal class BatchStrategyContext<TEntity> where TEntity : class
{
    private readonly DbContext _context;
    private int _roundTripCounter;

    internal BatchStrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;
    }

    internal DbContext Context => _context;
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

    internal int GetEntityId(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            throw new InvalidOperationException("Entity does not have a primary key");
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue is int id)
        {
            return id;
        }

        throw new InvalidOperationException("Primary key must be of type int");
    }

    internal BatchFailure CreateBatchFailure(int entityId, Exception exception)
    {
        var reason = exception switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };

        return new BatchFailure
        {
            EntityId = entityId,
            ErrorMessage = exception.Message,
            Reason = reason,
            Exception = exception
        };
    }

    internal void DetachEntity(TEntity entity)
    {
        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    internal void DetachAllEntities(List<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            DetachEntity(entity);
        }
    }
}
