namespace Winnow.Internal.Accumulators;

/// <summary>
/// Builds accumulator instances at the configured <see cref="ResultDetail"/>.
/// Centralised so the level switch lives in exactly one place; if the build
/// path drifts back into individual operation classes, the bool-flag pattern
/// rejected during architectural review re-emerges.
/// </summary>
internal static class AccumulatorFactory
{
    internal static InsertAccumulator<TKey> CreateInsert<TKey>(ResultDetail detail)
        where TKey : notnull, IEquatable<TKey> => new(detail);

    internal static WinnowAccumulator<TKey> CreateWinnow<TKey>(ResultDetail detail)
        where TKey : notnull, IEquatable<TKey> => new(detail);

    internal static UpsertAccumulator<TKey> CreateUpsert<TKey>(ResultDetail detail)
        where TKey : notnull, IEquatable<TKey> => new(detail);

    internal static GraphResultAccumulator<TKey> CreateGraph<TKey>(ResultDetail detail)
        where TKey : notnull, IEquatable<TKey> => new(detail);
}
