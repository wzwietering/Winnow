namespace Winnow.Internal.Validation;

/// <summary>
/// Outcome of a pre-validation pass over a single batch. Carries both the
/// survivor list and a translation from "current index in survivors" back to
/// "original index in caller-supplied input" so downstream stages can record
/// successes against the user-visible position.
/// </summary>
/// <remarks>
/// When no validation was configured, <see cref="OriginalIndices"/> is
/// <c>null</c> — <see cref="GetOriginalIndex"/> then returns the input index
/// unchanged. This keeps the no-validation path allocation-free for the
/// index map.
/// </remarks>
internal readonly struct PreValidationResult<TEntity> where TEntity : class
{
    /// <summary>Entities that passed validation, in order.</summary>
    internal List<TEntity> Survivors { get; }

    /// <summary>
    /// For each survivor in order, the index it occupied in the original input
    /// list. <c>null</c> when no validation was applied (callers should use the
    /// loop counter directly).
    /// </summary>
    internal int[]? OriginalIndices { get; }

    internal PreValidationResult(List<TEntity> survivors, int[]? originalIndices)
    {
        Survivors = survivors;
        OriginalIndices = originalIndices;
    }

    /// <summary>Returns the input list verbatim with no index translation.</summary>
    internal static PreValidationResult<TEntity> Passthrough(List<TEntity> entities) =>
        new(entities, originalIndices: null);

    internal int GetOriginalIndex(int survivorIndex) =>
        OriginalIndices is null ? survivorIndex : OriginalIndices[survivorIndex];
}
