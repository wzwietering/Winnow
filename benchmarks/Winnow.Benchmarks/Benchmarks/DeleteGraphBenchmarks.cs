using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures DeleteGraphBatch performance with a 3-level hierarchy.
/// Seeds fresh order graphs each iteration, then deletes them.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class DeleteGraphBenchmarks
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
        // Seed fresh order graphs
        using var seedContext = new BenchmarkDbContext(_options);
        seedContext.OrderReservations.ExecuteDelete();
        seedContext.OrderItems.ExecuteDelete();
        seedContext.Orders.ExecuteDelete();

        var seedOrders = EntityGenerator.CreateOrders(BatchSize);
        seedContext.Orders.AddRange(seedOrders);
        seedContext.SaveChanges();

        // Load into tracked context for deletion
        _context = new BenchmarkDbContext(_options);
        _orders = _context.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Reservations)
            .OrderBy(o => o.Id)
            .ToList();
    }

    [Benchmark]
    public BatchResult<int> DeleteGraphBatch()
    {
        var saver = new BatchSaver<BenchmarkOrder, int>(_context);
        return saver.DeleteGraphBatch(
            _orders,
            new DeleteGraphBatchOptions { Strategy = Strategy });
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
}
