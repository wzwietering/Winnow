using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class InsertBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000, 10000)]
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
        context.Products.ExecuteDelete();

        _products = EntityGenerator.CreateProducts(BatchSize);
    }

    [Benchmark]
    public InsertResult<int> Insert()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkProduct, int>(context);
        return saver.Insert(_products, new InsertOptions { Strategy = Strategy });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();
    }
}
