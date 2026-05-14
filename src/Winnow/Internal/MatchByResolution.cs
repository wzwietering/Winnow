using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal;

/// <summary>
/// Snapshot of all per-batch state produced by an upsert operation's MatchBy resolution.
/// Owned by the operation for the duration of a single batch and consumed by per-entity routing.
/// Pure data — the live <see cref="Microsoft.EntityFrameworkCore.DbContext"/> is available
/// via <c>StrategyContext.Context</c> at the call sites, so it doesn't belong here.
/// </summary>
internal sealed class MatchByResolution<TEntity> where TEntity : class
{
    internal MatchExpressionPlan<TEntity> Plan { get; }
    internal Dictionary<MatchKey, TEntity> ExistingByMatchKey { get; }
    internal object?[][] EntityMatchValues { get; }
    internal IReadOnlyList<IProperty> ConcurrencyTokens { get; }

    internal MatchByResolution(
        MatchExpressionPlan<TEntity> plan,
        object?[][] entityMatchValues,
        IReadOnlyList<IProperty> concurrencyTokens,
        Dictionary<MatchKey, TEntity> existingByMatchKey)
    {
        Plan = plan;
        EntityMatchValues = entityMatchValues;
        ConcurrencyTokens = concurrencyTokens;
        ExistingByMatchKey = existingByMatchKey;
    }
}
