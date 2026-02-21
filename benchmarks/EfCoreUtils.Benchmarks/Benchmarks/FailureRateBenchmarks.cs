using BenchmarkDotNet.Attributes;
using EfCoreUtils.Benchmarks.Entities;
using EfCoreUtils.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Benchmarks;

/// <summary>
/// Measures how error handling impacts performance.
/// Products with Price = -1 fail validation, triggering retry/recovery logic.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class FailureRateBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(0.0, 0.1, 0.25)]
    public double FailureRate { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private List<BenchmarkProduct> _products = null!;

    private const int BatchSize = 1000;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var connectionString = GlobalState.Containers.GetConnectionString(Provider);
        _options = DatabaseProviderFactory.CreateOptions(Provider, connectionString);

        using var context = new BenchmarkDbContext(_options);
        context.Database.EnsureCreated();
        _ = context.Products.FirstOrDefault();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();

        _products = FailureRate > 0
            ? EntityGenerator.CreateProductsWithFailures(BatchSize, FailureRate)
            : EntityGenerator.CreateProducts(BatchSize);
    }

    [Benchmark]
    public InsertBatchResult<int> InsertWithFailures()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new BatchSaver<BenchmarkProduct, int>(context);
        return saver.InsertBatch(
            _products,
            new InsertBatchOptions { Strategy = Strategy });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();
    }
}
