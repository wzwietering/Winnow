namespace EfCoreUtils.Internal;

internal static class DepthConstants
{
    internal const int AbsoluteMaxDepth = 100;

    internal static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);
}
