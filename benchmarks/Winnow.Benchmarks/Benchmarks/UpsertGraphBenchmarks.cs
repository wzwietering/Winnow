using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures UpsertGraph performance with a 3-level hierarchy.
/// Half the batch are existing orders (updates), half are new (inserts).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class UpsertGraphBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private BenchmarkDbContext _context = null!;
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
        var existingCount = BatchSize / 2;
        SeedOrders(existingCount);
        var existing = LoadAndMarkModified();
        var newOrders = EntityGenerator.CreateOrders(BatchSize - existingCount);
        _orders = [.. existing, .. newOrders];
    }

    [Benchmark]
    public UpsertResult<int> UpsertGraph()
    {
        var saver = new Winnower<BenchmarkOrder, int>(_context);
        return saver.UpsertGraph(
            _orders,
            new UpsertGraphOptions { Strategy = Strategy });
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _context.Dispose();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.OrderReservations.ExecuteDelete();
        context.OrderItems.ExecuteDelete();
        context.Orders.ExecuteDelete();
    }

    private void SeedOrders(int count)
    {
        using var seedContext = new BenchmarkDbContext(_options);
        seedContext.OrderReservations.ExecuteDelete();
        seedContext.OrderItems.ExecuteDelete();
        seedContext.Orders.ExecuteDelete();

        var seedOrders = EntityGenerator.CreateOrders(count);
        seedContext.Orders.AddRange(seedOrders);
        seedContext.SaveChanges();
    }

    private List<BenchmarkOrder> LoadAndMarkModified()
    {
        _context = new BenchmarkDbContext(_options);
        var existing = _context.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Reservations)
            .OrderBy(o => o.Id)
            .ToList();

        foreach (var order in existing)
            order.TotalAmount += 1;

        return existing;
    }
}
