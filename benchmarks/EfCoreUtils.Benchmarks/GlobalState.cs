using EfCoreUtils.Benchmarks.Infrastructure;

namespace EfCoreUtils.Benchmarks;

public static class GlobalState
{
    public static ContainerManager Containers { get; } = new();
}
