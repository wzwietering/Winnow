using BenchmarkDotNet.Attributes;
using Winnow.Benchmarks.Entities;
using Winnow.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Benchmarks.Benchmarks;

/// <summary>
/// Measures memory and time across <see cref="ResultDetail"/> levels for both
/// flat and graph inserts. The intent is to quantify the memory savings from
/// reducing detail at the configured batch sizes; CPU should remain comparable
/// since the same DivideAndConquer machinery runs in every mode.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 10, warmupCount: 3)]
public class ResultDetailBenchmarks
{
    [ParamsSource(nameof(Providers))]
    public DatabaseProvider Provider { get; set; }

    public static IEnumerable<DatabaseProvider> Providers => GlobalState.Containers.StartedProviders;

    [ParamsAllValues]
    public ResultDetail Detail { get; set; }

    [Params(1000, 5000)]
    public int BatchSize { get; set; }

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private List<BenchmarkProduct> _products = null!;
    private List<BenchmarkOrder> _orders = null!;

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
        context.OrderReservations.ExecuteDelete();
        context.OrderItems.ExecuteDelete();
        context.Orders.ExecuteDelete();
        context.Products.ExecuteDelete();

        _products = EntityGenerator.CreateProducts(BatchSize);
        _orders = EntityGenerator.CreateOrders(BatchSize);
    }

    [Benchmark]
    public InsertResult<int> Insert()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkProduct, int>(context);
        return saver.Insert(_products, new InsertOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            ResultDetail = Detail
        });
    }

    [Benchmark]
    public InsertResult<int> InsertGraph()
    {
        using var context = new BenchmarkDbContext(_options);
        var saver = new Winnower<BenchmarkOrder, int>(context);
        return saver.InsertGraph(_orders, new InsertGraphOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            ResultDetail = Detail
        });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        using var context = new BenchmarkDbContext(_options);
        context.OrderReservations.ExecuteDelete();
        context.OrderItems.ExecuteDelete();
        context.Orders.ExecuteDelete();
        context.Products.ExecuteDelete();
    }
}
