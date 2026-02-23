using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures ParallelWinnower async insert performance at different degrees of parallelism.
/// Uses DivideAndConquer strategy only (the superior strategy).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class ParallelInsertBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [Params(1, 2, 4, 8)]
    public int DegreeOfParallelism { get; set; }

    [Params(1000, 5000)]
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
    public async Task<InsertResult<int>> ParallelInsert()
    {
        var saver = new ParallelWinnower<BenchmarkProduct, int>(
            () => new BenchmarkDbContext(_options),
            DegreeOfParallelism);

        return await saver.InsertAsync(
            _products,
            new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();
    }
}
