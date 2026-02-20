using EfCoreUtils.Benchmarks.Entities;

namespace EfCoreUtils.Benchmarks.Infrastructure;

public static class EntityGenerator
{
    public static List<BenchmarkProduct> CreateProducts(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new BenchmarkProduct
            {
                Name = $"Product {i}",
                Price = 10 + i,
                Stock = i * 5
            })
            .ToList();

    public static List<BenchmarkProduct> CreateProductsWithFailures(int count, double failureRate)
    {
        var random = new Random(42);

        return Enumerable.Range(1, count)
            .Select(i => new BenchmarkProduct
            {
                Name = $"Product {i}",
                Price = random.NextDouble() < failureRate ? -1 : 10 + i,
                Stock = i * 5
            })
            .ToList();
    }
}
