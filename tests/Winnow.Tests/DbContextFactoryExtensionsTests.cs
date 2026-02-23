using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class DbContextFactoryExtensionsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"winnow_factory_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CreateParallelBatchSaver_WithExplicitKey_Works()
    {
        EnsureDatabase();
        var factory = CreateFactory();

        var saver = factory.CreateParallelBatchSaver<Product, int, TestDbContext>();

        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 },
            new() { Name = "P2", Price = 20, Stock = 2 }
        };
        var result = await saver.InsertBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    [Fact]
    public async Task CreateParallelBatchSaver_AutoDetect_Works()
    {
        EnsureDatabase();
        var factory = CreateFactory();

        var saver = factory.CreateParallelBatchSaver<Product, TestDbContext>();

        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 },
            new() { Name = "P2", Price = 20, Stock = 2 }
        };
        var result = await saver.InsertBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    private IDbContextFactory<TestDbContext> CreateFactory()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource={_dbPath}");
        return new SimpleDbContextFactory(optionsBuilder.Options);
    }

    private void EnsureDatabase()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource={_dbPath}")
            .Options;
        using var ctx = new TestDbContext(options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch { /* Best effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private class SimpleDbContextFactory(DbContextOptions<TestDbContext> options)
        : IDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateDbContext() => new(options);
    }
}
