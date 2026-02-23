using Microsoft.EntityFrameworkCore;

namespace Winnow.Internal;

/// <summary>
/// Factory for creating batch result objects with consistent structure.
/// </summary>
internal static class ResultFactory
{
    internal static WinnowResult<TKey> CreateEmpty<TKey>(TimeSpan duration, bool includeGraph = false)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            SuccessfulIds = [],
            Failures = [],
            Duration = duration,
            DatabaseRoundTrips = 0,
            GraphHierarchy = includeGraph ? [] : null,
            TraversalInfo = includeGraph ? CreateEmptyTraversalInfo<TKey>() : null
        };

    internal static WinnowResult<TKey> Enrich<TKey>(
        WinnowResult<TKey> result,
        TimeSpan duration,
        int roundTrips,
        int totalRetries = 0)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo,
            WasCancelled = result.WasCancelled,
            TotalRetries = totalRetries
        };

    internal static InsertResult<TKey> CreateEmptyInsert<TKey>(TimeSpan duration, bool includeGraph = false)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = [],
            Failures = [],
            Duration = duration,
            DatabaseRoundTrips = 0,
            GraphHierarchy = includeGraph ? [] : null,
            TraversalInfo = includeGraph ? CreateEmptyTraversalInfo<TKey>() : null
        };

    internal static InsertResult<TKey> EnrichInsert<TKey>(
        InsertResult<TKey> result,
        TimeSpan duration,
        int roundTrips,
        int totalRetries = 0)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo,
            WasCancelled = result.WasCancelled,
            TotalRetries = totalRetries
        };

    internal static UpsertResult<TKey> CreateEmptyUpsert<TKey>(TimeSpan duration, bool includeGraph = false)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = [],
            UpdatedEntities = [],
            Failures = [],
            Duration = duration,
            DatabaseRoundTrips = 0,
            GraphHierarchy = includeGraph ? [] : null,
            TraversalInfo = includeGraph ? CreateEmptyTraversalInfo<TKey>() : null
        };

    internal static UpsertResult<TKey> EnrichUpsert<TKey>(
        UpsertResult<TKey> result,
        TimeSpan duration,
        int roundTrips,
        int totalRetries = 0)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            InsertedEntities = result.InsertedEntities,
            UpdatedEntities = result.UpdatedEntities,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo,
            WasCancelled = result.WasCancelled,
            TotalRetries = totalRetries
        };

    private static GraphTraversalResult<TKey> CreateEmptyTraversalInfo<TKey>()
        where TKey : notnull, IEquatable<TKey> => new()
        {
            MaxDepthReached = 0,
            TotalEntitiesTraversed = 0,
            EntitiesByDepth = new Dictionary<int, int>()
        };

    internal static WinnowFailure<TKey> CreateWinnowFailure<TKey>(TKey entityId, Exception exception)
        where TKey : notnull, IEquatable<TKey> => new()
        {
            EntityId = entityId,
            ErrorMessage = exception.Message,
            Reason = FailureClassifier.Classify(exception),
            Exception = exception
        };

    internal static InsertFailure CreateInsertFailure(int entityIndex, Exception exception) => new()
        {
            EntityIndex = entityIndex,
            ErrorMessage = exception.Message,
            Reason = FailureClassifier.Classify(exception),
            Exception = exception
        };
}
