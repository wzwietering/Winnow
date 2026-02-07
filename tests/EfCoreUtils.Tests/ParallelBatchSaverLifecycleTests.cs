using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ParallelBatchSaverLifecycleTests : ParallelTestBase
{
    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ParallelBatchSaver<Product, int>(null!, 2));
    }

    [Fact]
    public void Constructor_ZeroParallelism_ThrowsArgumentOutOfRangeException()
    {
        EnsureDatabaseCreated();
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ParallelBatchSaver<Product, int>(CreateContextFactory(), 0));
    }

    [Fact]
    public void Constructor_NegativeParallelism_ThrowsArgumentOutOfRangeException()
    {
        EnsureDatabaseCreated();
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ParallelBatchSaver<Product, int>(CreateContextFactory(), -1));
    }

    [Fact]
    public void Constructor_FactoryReturnsSameInstance_ThrowsArgumentException()
    {
        EnsureDatabaseCreated();
        var context = (DbContext)CreateContextFactory()();

        Should.Throw<ArgumentException>(() =>
            new ParallelBatchSaver<Product, int>(() => context, 2));

        context.Dispose();
    }

    [Fact]
    public async Task MaxDegreeOfParallelism_One_UsesSequentialPath()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 1);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 1;

        var result = await saver.UpdateBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public async Task MaxDegreeOfParallelism_LargerThanEntityCount_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 2));

        var saver = CreateSaver(maxDegreeOfParallelism: 10);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 1;

        var result = await saver.UpdateBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
    }

    [Fact]
    public async Task MaxDegreeOfParallelism_Two_MinimumParallelism_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 1;

        var result = await saver.UpdateBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        EnsureDatabaseCreated();
        var saver = CreateSaver();

        Should.NotThrow(() => saver.Dispose());
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        EnsureDatabaseCreated();
        var saver = CreateSaver();

        await Should.NotThrowAsync(async () => await saver.DisposeAsync());
    }

    [Fact]
    public void SyncMethods_UseSingleContext_NoParallelism()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 1;

        var result = saver.UpdateBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void AutoDetectVariant_Product_Works()
    {
        EnsureDatabaseCreated();
        var saver = new ParallelBatchSaver<Product>(CreateContextFactory(), 2);

        saver.IsCompositeKey.ShouldBeFalse();
        saver.MaxDegreeOfParallelism.ShouldBe(2);
    }

    [Fact]
    public void AutoDetectVariant_OrderLine_DetectsCompositeKey()
    {
        EnsureDatabaseCreated();
        var saver = new ParallelBatchSaver<OrderLine>(CreateContextFactory(), 2);

        saver.IsCompositeKey.ShouldBeTrue();
    }

    [Fact]
    public void FactoryExtension_CreateBatchSaver_Works()
    {
        EnsureDatabaseCreated();
        var factory = new TestDbContextFactory(DbPath);

        var saver = factory.CreateBatchSaver<Product, int, TestDbContext>(2);

        saver.MaxDegreeOfParallelism.ShouldBe(2);
    }

    private class TestDbContextFactory(string path) : IDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite($"DataSource={path}")
                .Options;
            return new TestDbContext(options);
        }
    }
}
