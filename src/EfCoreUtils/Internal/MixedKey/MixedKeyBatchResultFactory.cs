using EfCoreUtils.MixedKey;

namespace EfCoreUtils.Internal.MixedKey;

/// <summary>
/// Factory for creating mixed-key batch result objects with consistent structure.
/// </summary>
internal static class MixedKeyBatchResultFactory
{
    internal static MixedKeyBatchResult CreateEmpty(TimeSpan duration) => new()
    {
        SuccessfulIds = [],
        Failures = [],
        Duration = duration,
        DatabaseRoundTrips = 0,
        GraphHierarchy = [],
        TraversalInfo = CreateEmptyTraversalInfo()
    };

    internal static MixedKeyBatchResult Enrich(
        MixedKeyBatchResult result,
        TimeSpan duration,
        int roundTrips) => new()
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };

    internal static MixedKeyInsertBatchResult CreateEmptyInsert(TimeSpan duration) => new()
    {
        InsertedEntities = [],
        Failures = [],
        Duration = duration,
        DatabaseRoundTrips = 0,
        GraphHierarchy = [],
        TraversalInfo = CreateEmptyTraversalInfo()
    };

    internal static MixedKeyInsertBatchResult EnrichInsert(
        MixedKeyInsertBatchResult result,
        TimeSpan duration,
        int roundTrips) => new()
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = duration,
            DatabaseRoundTrips = roundTrips,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };

    private static MixedKeyGraphTraversalResult CreateEmptyTraversalInfo() => new()
    {
        MaxDepthReached = 0,
        TotalEntitiesTraversed = 0,
        EntitiesByDepth = new Dictionary<int, int>(),
        EntitiesByKeyType = new Dictionary<Type, int>()
    };
}
