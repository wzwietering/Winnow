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
}
