using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Tests.Infrastructure;

public abstract class TestBase : IDisposable
{
    protected TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        return context;
    }

    protected void SeedData(TestDbContext context, int productCount = 10)
    {
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(productCount);

        context.Products.AddRange(products);
        context.SaveChanges();

        context.ChangeTracker.Clear();
    }

    protected void CleanupContext(TestDbContext context)
    {
        context.Database.EnsureDeleted();
        context.Dispose();
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
