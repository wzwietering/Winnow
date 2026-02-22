using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Compares raw EF Core SaveChanges against the library's DivideAndConquer strategy
/// to measure the overhead/benefit of the library itself.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class BaselineInsertBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [Params(100, 1000, 5000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;

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
    }

    [Benchmark(Baseline = true)]
    public int RawEfCore()
    {
        using var context = new BenchmarkDbContext(_options);
        var products = EntityGenerator.CreateProducts(BatchSize);
        context.Products.AddRange(products);
        return context.SaveChanges();
    }

    [Benchmark]
    public InsertBatchResult<int> LibraryDivideAndConquer()
    {
        using var context = new BenchmarkDbContext(_options);
        var products = EntityGenerator.CreateProducts(BatchSize);
        var saver = new BatchSaver<BenchmarkProduct, int>(context);
        return saver.InsertBatch(
            products,
            new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });
    }

    [Benchmark]
    public InsertBatchResult<int> LibraryOneByOne()
    {
        using var context = new BenchmarkDbContext(_options);
        var products = EntityGenerator.CreateProducts(BatchSize);
        var saver = new BatchSaver<BenchmarkProduct, int>(context);
        return saver.InsertBatch(
            products,
            new InsertBatchOptions { Strategy = BatchStrategy.OneByOne });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();
    }
}
