namespace Winnow.Internal;

/// <summary>
/// Bundles traversal configuration extracted from options.
/// Operations create this once and pass it through the call chain.
/// </summary>
internal sealed class TraversalContext
{
    internal int MaxDepth { get; init; }
    internal NavigationFilter? NavigationFilter { get; init; }
    internal CircularReferenceHandling CircularReferenceHandling { get; init; }

    internal static TraversalContext FromOptions(GraphBatchOptionsBase options) => new()
    {
        MaxDepth = DepthConstants.ClampDepth(options.MaxDepth),
        NavigationFilter = options.NavigationFilter,
        CircularReferenceHandling = options.CircularReferenceHandling,
    };
}
