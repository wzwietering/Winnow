using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

public class BatchSaver<TEntity>(DbContext context) : IBatchSaver<TEntity> where TEntity : class
{
    private readonly DbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private int _roundTripCounter;

    public BatchResult UpdateBatch(IEnumerable<TEntity> entities)
    {
        return UpdateBatch(entities, new BatchOptions());
    }

    public BatchResult UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        _roundTripCounter = 0;

        var entityList = entities.ToList();
        if (entityList.Count == 0)
        {
            return new BatchResult
            {
                SuccessfulIds = Array.Empty<int>(),
                Failures = Array.Empty<BatchFailure>(),
                Duration = stopwatch.Elapsed,
                DatabaseRoundTrips = 0
            };
        }

        BatchResult result = options.Strategy switch
        {
            BatchStrategy.OneByOne => OneByOneStrategy(entityList),
            BatchStrategy.DivideAndConquer => DivideAndConquerStrategy(entityList),
            _ => throw new ArgumentException($"Unknown strategy: {options.Strategy}")
        };

        stopwatch.Stop();

        return new BatchResult
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = _roundTripCounter
        };
    }

    public Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities));
    }

    public Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities, options));
    }

    private BatchResult OneByOneStrategy(List<TEntity> entities)
    {
        var successfulIds = new List<int>();
        var failures = new List<BatchFailure>();

        // Detach all entities first to ensure clean slate
        foreach (var entity in entities)
        {
            var entry = _context.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }

        foreach (var entity in entities)
        {
            try
            {
                _context.Entry(entity).State = EntityState.Modified;
                _context.SaveChanges();
                _roundTripCounter++;

                var entityId = GetEntityId(entity);
                successfulIds.Add(entityId);

                _context.Entry(entity).State = EntityState.Detached;
            }
            catch (Exception ex)
            {
                _roundTripCounter++;
                var entityId = GetEntityId(entity);
                var failure = CreateBatchFailure(entityId, ex);
                failures.Add(failure);

                var entry = _context.Entry(entity);
                if (entry.State != EntityState.Detached)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        return new BatchResult
        {
            SuccessfulIds = successfulIds,
            Failures = failures
        };
    }

    private BatchResult DivideAndConquerStrategy(List<TEntity> entities)
    {
        var successfulIds = new List<int>();
        var failures = new List<BatchFailure>();

        // Detach all entities first to ensure clean slate
        foreach (var entity in entities)
        {
            var entry = _context.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }

        ProcessBatch(entities, successfulIds, failures);

        return new BatchResult
        {
            SuccessfulIds = successfulIds,
            Failures = failures
        };
    }

    private void ProcessBatch(List<TEntity> entities, List<int> successfulIds, List<BatchFailure> failures)
    {
        if (entities.Count == 0)
            return;

        if (entities.Count == 1)
        {
            var entity = entities[0];
            try
            {
                _context.Entry(entity).State = EntityState.Modified;
                _context.SaveChanges();
                _roundTripCounter++;

                var entityId = GetEntityId(entity);
                successfulIds.Add(entityId);

                _context.Entry(entity).State = EntityState.Detached;
            }
            catch (Exception ex)
            {
                _roundTripCounter++;
                var entityId = GetEntityId(entity);
                var failure = CreateBatchFailure(entityId, ex);
                failures.Add(failure);

                var entry = _context.Entry(entity);
                if (entry.State != EntityState.Detached)
                {
                    entry.State = EntityState.Detached;
                }
            }
            return;
        }

        try
        {
            foreach (var entity in entities)
            {
                _context.Entry(entity).State = EntityState.Modified;
            }

            _context.SaveChanges();
            _roundTripCounter++;

            foreach (var entity in entities)
            {
                var entityId = GetEntityId(entity);
                successfulIds.Add(entityId);
                _context.Entry(entity).State = EntityState.Detached;
            }
        }
        catch
        {
            _roundTripCounter++;

            foreach (var entity in entities)
            {
                _context.Entry(entity).State = EntityState.Detached;
            }

            var midpoint = entities.Count / 2;
            var firstHalf = entities.Take(midpoint).ToList();
            var secondHalf = entities.Skip(midpoint).ToList();

            ProcessBatch(firstHalf, successfulIds, failures);
            ProcessBatch(secondHalf, successfulIds, failures);
        }
    }

    private int GetEntityId(TEntity entity)
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

    private BatchFailure CreateBatchFailure(int entityId, Exception exception)
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
}
