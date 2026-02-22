using Winnow.Benchmarks.Infrastructure;

namespace Winnow.Benchmarks;

public static class GlobalState
{
    public static ContainerManager Containers { get; } = new();
}
