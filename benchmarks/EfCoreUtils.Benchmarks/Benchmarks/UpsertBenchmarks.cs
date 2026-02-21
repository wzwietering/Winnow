using BenchmarkDotNet.Attributes;
using EfCoreUtils.Benchmarks.Entities;
using EfCoreUtils.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class UpsertBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000, 10000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private BenchmarkDbContext _context = null!;
    private List<BenchmarkProduct> _products = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var connectionString = GlobalState.Containers.GetConnectionString(Provider);
        _options = DatabaseProviderFactory.CreateOptions(Provider, connectionString);

        using var context = new BenchmarkDbContext(_options);
        context.Database.EnsureCreated();

        // Warmup query
        _ = context.Products.FirstOrDefault();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Clear and seed half the batch as existing entities
        using var seedContext = new BenchmarkDbContext(_options);
        seedContext.Products.ExecuteDelete();

        var existingCount = BatchSize / 2;
        var seedProducts = EntityGenerator.CreateProducts(existingCount);
        seedContext.Products.AddRange(seedProducts);
        seedContext.SaveChanges();

        // Load existing entities (will have IDs → treated as updates)
        _context = new BenchmarkDbContext(_options);
        var existing = _context.Products.OrderBy(p => p.Id).ToList();
        foreach (var product in existing)
            product.Price += 1;

        // Generate new entities (Id = 0 → treated as inserts)
        var newProducts = EntityGenerator.CreateProducts(BatchSize - existingCount);

        _products = [.. existing, .. newProducts];
    }

    [Benchmark]
    public UpsertBatchResult<int> UpsertBatch()
    {
        var saver = new BatchSaver<BenchmarkProduct, int>(_context);
        return saver.UpsertBatch(_products, new UpsertBatchOptions { Strategy = Strategy });
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
        context.Products.ExecuteDelete();
    }
}
