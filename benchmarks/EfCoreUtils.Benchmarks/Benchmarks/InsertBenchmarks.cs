using BenchmarkDotNet.Attributes;
using EfCoreUtils.Benchmarks.Entities;
using EfCoreUtils.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class InsertBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private List<BenchmarkProduct> _products = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var connectionString = GlobalState.Containers.GetConnectionString(Provider);
        _options = DatabaseProviderFactory.CreateOptions(Provider, connectionString);

        using var context = new BenchmarkDbContext(_options);
        context.Database.EnsureCreated();

        // Warmup query to establish connection
        _ = context.Products.FirstOrDefault();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Database.ExecuteSqlRaw("DELETE FROM Products");

        _products = EntityGenerator.CreateProducts(BatchSize);
    }

    [Benchmark]
    public InsertBatchResult<int> InsertBatch()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new BatchSaver<BenchmarkProduct, int>(context);
        return saver.InsertBatch(_products, new InsertBatchOptions { Strategy = Strategy });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Database.EnsureDeleted();
    }
}
