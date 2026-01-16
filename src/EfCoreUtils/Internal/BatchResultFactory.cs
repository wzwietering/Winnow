using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Internal;

/// <summary>
/// Factory for creating batch result objects with consistent structure.
/// </summary>
internal static class BatchResultFactory
{
    internal static BatchResult<TKey> CreateEmpty<TKey>(TimeSpan duration, bool includeGraph = false)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            SuccessfulIds = [],
            Failures = [],
            Duration = duration,
            DatabaseRoundTrips = 0,
            GraphHierarchy = includeGraph ? [] : null,
            TraversalInfo = includeGraph ? CreateEmptyTraversalInfo<TKey>() : null
        };

    internal static BatchResult<TKey> Enrich<TKey>(
        BatchResult<TKey> result,
        TimeSpan duration,
        int roundTrips)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };

    internal static InsertBatchResult<TKey> CreateEmptyInsert<TKey>(TimeSpan duration, bool includeGraph = false)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = [],
            Failures = [],
            Duration = duration,
            DatabaseRoundTrips = 0,
            GraphHierarchy = includeGraph ? [] : null,
            TraversalInfo = includeGraph ? CreateEmptyTraversalInfo<TKey>() : null
        };

    internal static InsertBatchResult<TKey> EnrichInsert<TKey>(
        InsertBatchResult<TKey> result,
        TimeSpan duration,
        int roundTrips)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };

    private static GraphTraversalResult<TKey> CreateEmptyTraversalInfo<TKey>()
        where TKey : notnull, IEquatable<TKey> => new()
        {
            MaxDepthReached = 0,
            TotalEntitiesTraversed = 0,
            EntitiesByDepth = new Dictionary<int, int>()
        };

    internal static BatchFailure<TKey> CreateBatchFailure<TKey>(TKey entityId, Exception exception)
        where TKey : notnull, IEquatable<TKey>
    {
        var reason = DetermineFailureReason(exception);
        return new BatchFailure<TKey>
        {
            EntityId = entityId,
            ErrorMessage = exception.Message,
            Reason = reason,
            Exception = exception
        };
    }

    internal static InsertBatchFailure CreateInsertBatchFailure(int entityIndex, Exception exception)
    {
        var reason = DetermineFailureReason(exception);
        return new InsertBatchFailure
        {
            EntityIndex = entityIndex,
            ErrorMessage = exception.Message,
            Reason = reason,
            Exception = exception
        };
    }

    private static FailureReason DetermineFailureReason(Exception exception) => exception switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
