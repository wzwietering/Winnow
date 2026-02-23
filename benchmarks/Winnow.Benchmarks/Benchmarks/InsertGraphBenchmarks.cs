using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures InsertGraph performance with a 3-level hierarchy:
/// BenchmarkOrder → BenchmarkOrderItem → BenchmarkOrderReservation.
/// Each root entity has 2 items, each item has 1 reservation (5 entities per root).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class InsertGraphBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private List<BenchmarkOrder> _orders = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var connectionString = GlobalState.Containers.GetConnectionString(Provider);
        _options = DatabaseProviderFactory.CreateOptions(Provider, connectionString);

        using var context = new BenchmarkDbContext(_options);
        context.Database.EnsureCreated();
        _ = context.Orders.FirstOrDefault();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.OrderReservations.ExecuteDelete();
        context.OrderItems.ExecuteDelete();
        context.Orders.ExecuteDelete();

        _orders = EntityGenerator.CreateOrders(BatchSize);
    }

    [Benchmark]
    public InsertResult<int> InsertGraph()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkOrder, int>(context);
        return saver.InsertGraph(
            _orders,
            new InsertGraphOptions { Strategy = Strategy });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.OrderReservations.ExecuteDelete();
        context.OrderItems.ExecuteDelete();
        context.Orders.ExecuteDelete();
    }
}
