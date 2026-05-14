using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures how pre-validation preserves the DivideAndConquer speed advantage
/// when batches contain invalid entities. Mirrors the entity shape and failure
/// generator used by <see cref="FailureRateBenchmarks"/> so the numbers compare
/// directly against the README's failure-rate table.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class PreValidationBenchmarks
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

    /// <summary>Baseline: no pre-validation — the existing 1.2.0 behaviour.</summary>
    [Benchmark(Baseline = true)]
    public InsertResult<int> InsertWithoutPreValidation()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkProduct, int>(context);
        return saver.Insert(
            _products,
            new InsertOptions { Strategy = Strategy });
    }

    /// <summary>Pre-validation via inline delegate (the canonical fast path).</summary>
    [Benchmark]
    public InsertResult<int> InsertWithDelegateValidation()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkProduct, int>(context);
        var options = new InsertOptions { Strategy = Strategy };
        options.WithValidation<BenchmarkProduct>((BenchmarkProduct p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(BenchmarkProduct.Price), "Must be positive");
        });
        return saver.Insert(_products, options);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.Products.ExecuteDelete();
    }
}
