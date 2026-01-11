using EfCoreUtils.Tests.Entities;

namespace EfCoreUtils.Tests.Infrastructure;

public class TestDataBuilder
{
    public List<Product> CreateValidProducts(int count)
    {
        var products = new List<Product>();
        for (int i = 1; i <= count; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Price = 10.00m + i,
                Stock = 100 + i,
                LastModified = DateTimeOffset.UtcNow,
                Version = new byte[8]
            });
        }
        return products;
    }

    public List<Product> CreateProductsWithInvalidPrices(int totalCount, int invalidCount)
    {
        var products = new List<Product>();
        for (int i = 1; i <= totalCount; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Price = i <= invalidCount ? -10.00m : 10.00m + i,
                Stock = 100 + i,
                LastModified = DateTimeOffset.UtcNow,
                Version = new byte[8]
            });
        }
        return products;
    }

    public List<Product> CreateProductsWithInvalidStock(int totalCount, int invalidCount)
    {
        var products = new List<Product>();
        for (int i = 1; i <= totalCount; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Price = 10.00m + i,
                Stock = i <= invalidCount ? -5 : 100 + i,
                LastModified = DateTimeOffset.UtcNow,
                Version = new byte[8]
            });
        }
        return products;
    }

    public List<Product> CreateMixedValidityProducts(int totalCount, int invalidCount)
    {
        return CreateProductsWithInvalidPrices(totalCount, invalidCount);
    }

    public List<Product> CreateProductsForConcurrencyTest(int count)
    {
        var products = new List<Product>();
        for (int i = 1; i <= count; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Price = 10.00m + i,
                Stock = 100 + i,
                LastModified = DateTimeOffset.UtcNow,
                Version = new byte[8]
            });
        }
        return products;
    }

    public void SeedDatabase(TestDbContext context, int productCount)
    {
        var products = CreateValidProducts(productCount);
        context.Products.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    public void SimulateConcurrentUpdate(TestDbContext context, int productId)
    {
        var product = context.Products.Find(productId);
        if (product != null)
        {
            product.Price += 1.00m;
            context.SaveChanges();
        }
    }

    public List<CustomerOrder> CreateValidCustomerOrders(int count, int itemsPerOrder = 3)
    {
        var orders = new List<CustomerOrder>();
        for (int i = 1; i <= count; i++)
        {
            var items = CreateOrderItems(i, itemsPerOrder);
            var totalAmount = items.Sum(item => item.Subtotal);

            orders.Add(new CustomerOrder
            {
                Id = i,
                OrderNumber = $"ORD-{i:D6}",
                CustomerId = 1000 + i,
                CustomerName = $"Customer {i}",
                Status = CustomerOrderStatus.Pending,
                TotalAmount = totalAmount,
                OrderDate = DateTimeOffset.UtcNow.AddDays(-i),
                OrderItems = items
            });
        }
        return orders;
    }

    private List<OrderItem> CreateOrderItems(int orderId, int count)
    {
        var items = new List<OrderItem>();
        for (int i = 1; i <= count; i++)
        {
            var quantity = i + 1;
            var unitPrice = 10.00m + i;
            items.Add(new OrderItem
            {
                Id = (orderId * 100) + i,
                CustomerOrderId = orderId,
                ProductId = 1000 + i,
                ProductName = $"Product {i}",
                Quantity = quantity,
                UnitPrice = unitPrice,
                Subtotal = quantity * unitPrice
            });
        }
        return items;
    }

    public List<CustomerOrder> CreateOrdersWithInvalidTotalAmount(int totalCount, int invalidCount)
    {
        var orders = CreateValidCustomerOrders(totalCount);
        for (int i = 0; i < invalidCount && i < orders.Count; i++)
        {
            orders[i].TotalAmount = -100.00m;
        }
        return orders;
    }

    public List<CustomerOrder> CreateOrdersWithInvalidItems(int totalCount, int invalidCount)
    {
        var orders = CreateValidCustomerOrders(totalCount);
        for (int i = 0; i < invalidCount && i < orders.Count; i++)
        {
            var firstItem = orders[i].OrderItems.FirstOrDefault();
            if (firstItem != null)
            {
                firstItem.Quantity = -5;
            }
        }
        return orders;
    }

    public void SeedCustomerOrders(TestDbContext context, int orderCount, int itemsPerOrder = 3)
    {
        var orders = CreateValidCustomerOrders(orderCount, itemsPerOrder);
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
