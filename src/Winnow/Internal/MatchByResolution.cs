using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal;

/// <summary>
/// Snapshot of all per-batch state produced by an upsert operation's MatchBy resolution.
/// Owned by the operation for the duration of a single batch and consumed by per-entity routing.
/// Bundling these together makes the lifecycle explicit (one field instead of five) and lets
/// callers detect at a glance whether resolution has happened.
/// </summary>
internal sealed class MatchByResolution<TEntity> where TEntity : class
{
    internal MatchExpressionPlan<TEntity> Plan { get; }
    internal Dictionary<MatchKey, TEntity> ExistingByMatchKey { get; }
    internal object?[][] EntityMatchValues { get; }
    internal DbContext DbContext { get; }
    internal IReadOnlyList<IProperty> ConcurrencyTokens { get; }

    internal MatchByResolution(
        MatchExpressionPlan<TEntity> plan,
        object?[][] entityMatchValues,
        DbContext dbContext,
        IReadOnlyList<IProperty> concurrencyTokens,
        Dictionary<MatchKey, TEntity> existingByMatchKey)
    {
        Plan = plan;
        EntityMatchValues = entityMatchValues;
        DbContext = dbContext;
        ConcurrencyTokens = concurrencyTokens;
        ExistingByMatchKey = existingByMatchKey;
    }
}
