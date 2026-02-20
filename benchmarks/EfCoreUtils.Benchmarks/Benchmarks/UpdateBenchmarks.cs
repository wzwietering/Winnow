using BenchmarkDotNet.Attributes;
using EfCoreUtils.Benchmarks.Entities;
using EfCoreUtils.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class UpdateBenchmarks
{
    private const int MaxBatchSize = 5000;

    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public BatchStrategy Strategy { get; set; }

    [Params(100, 1000, 5000)]
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

        // Seed max batch size worth of products
        var seedProducts = EntityGenerator.CreateProducts(MaxBatchSize);
        context.Products.AddRange(seedProducts);
        context.SaveChanges();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Reset all prices back to original values
        using var resetContext = new BenchmarkDbContext(_options);
        resetContext.Database.ExecuteSqlRaw("UPDATE Products SET Price = 10 + Id");

        // Load and modify products in a fresh tracked context
        _context = new BenchmarkDbContext(_options);
        _products = _context.Products
            .OrderBy(p => p.Id)
            .Take(BatchSize)
            .ToList();

        foreach (var product in _products)
            product.Price += 1;
    }

    [Benchmark]
    public BatchResult<int> UpdateBatch()
    {
        var saver = new BatchSaver<BenchmarkProduct, int>(_context);
        return saver.UpdateBatch(_products, new BatchOptions { Strategy = Strategy });
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
        context.Database.EnsureDeleted();
    }
}
