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

    public static List<BenchmarkOrder> CreateOrders(
        int count,
        int itemsPerOrder = 2,
        int reservationsPerItem = 1)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateOrder(i, itemsPerOrder, reservationsPerItem))
            .ToList();
    }

    private static BenchmarkOrder CreateOrder(
        int index,
        int itemsPerOrder,
        int reservationsPerItem)
    {
        var items = Enumerable.Range(1, itemsPerOrder)
            .Select(j => CreateOrderItem(index, j, reservationsPerItem))
            .ToList();

        return new BenchmarkOrder
        {
            OrderNumber = $"ORD-{index:D6}",
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity),
            Items = items
        };
    }

    private static BenchmarkOrderItem CreateOrderItem(
        int orderIndex,
        int itemIndex,
        int reservationsPerItem)
    {
        return new BenchmarkOrderItem
        {
            ProductName = $"Product {orderIndex}-{itemIndex}",
            Quantity = itemIndex,
            UnitPrice = 10 + itemIndex,
            Reservations = Enumerable.Range(1, reservationsPerItem)
                .Select(k => new BenchmarkOrderReservation
                {
                    WarehouseLocation = $"WH-{k}",
                    ReservedQuantity = itemIndex
                })
                .ToList()
        };
    }
}
