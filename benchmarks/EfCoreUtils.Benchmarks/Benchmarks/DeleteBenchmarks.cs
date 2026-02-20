using BenchmarkDotNet.Attributes;
using EfCoreUtils.Benchmarks.Entities;
using EfCoreUtils.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class DeleteBenchmarks
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
        // Seed fresh entities for each iteration
        using var seedContext = new BenchmarkDbContext(_options);
        seedContext.Database.ExecuteSqlRaw("DELETE FROM Products");

        var seedProducts = EntityGenerator.CreateProducts(BatchSize);
        seedContext.Products.AddRange(seedProducts);
        seedContext.SaveChanges();

        // Load into tracked context for deletion
        _context = new BenchmarkDbContext(_options);
        _products = _context.Products.OrderBy(p => p.Id).ToList();
    }

    [Benchmark]
    public BatchResult<int> DeleteBatch()
    {
        var saver = new BatchSaver<BenchmarkProduct, int>(_context);
        return saver.DeleteBatch(_products, new DeleteBatchOptions { Strategy = Strategy });
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
