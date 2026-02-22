using Winnow.Internal;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Winnow.Tests;

/// <summary>
/// Tests that exercise the actual retry loop with transient failures.
/// Uses a DbContext subclass that throws "database is locked" N times before succeeding.
/// </summary>
public class RetryBehaviorTests : IDisposable
{
    private TransientFailureDbContext CreateFailingContext(int failuresBeforeSuccess)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TransientFailureDbContext(options, failuresBeforeSuccess);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    // === SaveChangesRetryHandler direct tests ===

    [Fact]
    public void Sync_retry_succeeds_after_transient_failures()
    {
        using var context = CreateFailingContext(2);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });
        var retryCount = 0;

        SaveChangesRetryHandler.SaveWithRetry(
            context,
            new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero },
            null,
            () => retryCount++);

        retryCount.ShouldBe(2);
        context.Products.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Async_retry_succeeds_after_transient_failures()
    {
        using var context = CreateFailingContext(2);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });
        var retryCount = 0;

        await SaveChangesRetryHandler.SaveWithRetryAsync(
            context,
            new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero },
            null,
            () => retryCount++,
            CancellationToken.None);

        retryCount.ShouldBe(2);
        context.Products.Count().ShouldBe(1);
    }

    [Fact]
    public void Retry_exhausted_throws_original_exception()
    {
        using var context = CreateFailingContext(5);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });

        var ex = Should.Throw<DbUpdateException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.Zero },
                null,
                () => { }));

        ex.Message.ShouldContain("database is locked");
    }

    [Fact]
    public async Task Async_retry_exhausted_throws_original_exception()
    {
        using var context = CreateFailingContext(5);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });

        var ex = await Should.ThrowAsync<DbUpdateException>(() =>
            SaveChangesRetryHandler.SaveWithRetryAsync(
                context,
                new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.Zero },
                null,
                () => { },
                CancellationToken.None));

        ex.Message.ShouldContain("database is locked");
    }

    [Fact]
    public async Task Async_retry_does_not_retry_cancellation()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            SaveChangesRetryHandler.SaveWithRetryAsync(
                context,
                new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero },
                null,
                () => { },
                cts.Token));
    }

    [Fact]
    public void Non_transient_exception_not_retried()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Bad", Price = -1, Stock = 1 });
        var retryCount = 0;

        Should.Throw<InvalidOperationException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero },
                null,
                () => retryCount++));

        retryCount.ShouldBe(0);
    }

    // === Custom IsTransient predicate tests ===

    [Fact]
    public void Custom_IsTransient_replaces_built_in_classifier()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Bad", Price = -1, Stock = 1 });
        var customCalled = false;

        Should.Throw<InvalidOperationException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions
                {
                    MaxRetries = 3,
                    InitialDelay = TimeSpan.Zero,
                    IsTransient = ex =>
                    {
                        customCalled = true;
                        return false;
                    }
                },
                null,
                () => { }));

        customCalled.ShouldBeTrue();
    }

    [Fact]
    public void Custom_IsTransient_can_disable_built_in_transient_detection()
    {
        using var context = CreateFailingContext(1);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });
        var retryCount = 0;

        // "database is locked" is normally transient, but custom predicate disables it
        Should.Throw<DbUpdateException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions
                {
                    MaxRetries = 3,
                    InitialDelay = TimeSpan.Zero,
                    IsTransient = _ => false
                },
                null,
                () => retryCount++));

        retryCount.ShouldBe(0);
    }

    [Fact]
    public void Custom_IsTransient_can_mark_non_transient_as_retriable()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Bad", Price = -1, Stock = 1 });
        var retryCount = 0;

        Should.Throw<InvalidOperationException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions
                {
                    MaxRetries = 1,
                    InitialDelay = TimeSpan.Zero,
                    IsTransient = _ => true
                },
                null,
                () => retryCount++));

        retryCount.ShouldBe(1);
    }

    // === Integration through BatchSaver ===

    [Fact]
    public void InsertBatch_tracks_TotalRetries()
    {
        using var context = CreateFailingContext(2);
        var saver = new BatchSaver<Product, int>(context);

        var result = saver.InsertBatch(
            [new Product { Name = "P1", Price = 10, Stock = 1 }],
            new InsertBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(2);
    }

    [Fact]
    public async Task InsertBatchAsync_tracks_TotalRetries()
    {
        using var context = CreateFailingContext(1);
        var saver = new BatchSaver<Product, int>(context);

        var result = await saver.InsertBatchAsync(
            [new Product { Name = "P1", Price = 10, Stock = 1 }],
            new InsertBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(1);
    }

    [Fact]
    public void Retry_logs_each_attempt()
    {
        using var context = CreateFailingContext(2);
        var logger = new ListLogger();
        var saver = new BatchSaver<Product, int>(context, logger);

        saver.InsertBatch(
            [new Product { Name = "P1", Price = 10, Stock = 1 }],
            new InsertBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        var retryLogs = logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("Retry attempt"))
            .ToList();

        retryLogs.Count.ShouldBe(2);
        retryLogs[0].Message.ShouldContain("1/3");
        retryLogs[1].Message.ShouldContain("2/3");
    }

    [Fact]
    public void UpdateBatch_tracks_TotalRetries()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Seed", Price = 10, Stock = 1 });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var product = context.Products.First();
        product.Price = 99.99m;
        context.ChangeTracker.Clear();

        // Set failures AFTER seed
        context.FailuresRemaining = 1;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(
            [product],
            new BatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(1);
    }

    [Fact]
    public void UpsertBatch_tracks_TotalRetries()
    {
        using var context = CreateFailingContext(1);
        var saver = new BatchSaver<Product, int>(context);

        var result = saver.UpsertBatch(
            [new Product { Name = "New", Price = 10, Stock = 1 }],
            new UpsertBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(1);
    }

    [Fact]
    public void DeleteBatch_tracks_TotalRetries()
    {
        using var context = CreateFailingContext(0);
        context.Products.Add(new Product { Name = "Seed", Price = 10, Stock = 1 });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var product = context.Products.First();
        context.ChangeTracker.Clear();

        // Set failures AFTER seed
        context.FailuresRemaining = 1;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.DeleteBatch(
            [product],
            new DeleteBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(1);
    }

    // === Parallel path ===

    [Fact]
    public async Task ParallelInsertBatchAsync_tracks_TotalRetries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"winnow_retry_{Guid.NewGuid():N}.db");
        try
        {
            CreateSchemaOnDisk(dbPath);

            Func<DbContext> factory = () => CreateFailingContextOnDisk(dbPath, 1);
            var saver = new ParallelBatchSaver<Product, int>(factory, maxDegreeOfParallelism: 2);
            var products = Enumerable.Range(0, 4)
                .Select(i => new Product { Name = $"P{i}", Price = 10 + i, Stock = 1 })
                .ToList();

            var result = await saver.InsertBatchAsync(products, new InsertBatchOptions
            {
                Retry = new RetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
            });

            result.SuccessCount.ShouldBe(4);
            result.TotalRetries.ShouldBeGreaterThan(0);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    // === MaxRetries boundary ===

    [Fact]
    public void MaxRetries_zero_no_retry_on_transient()
    {
        using var context = CreateFailingContext(1);
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });

        Should.Throw<DbUpdateException>(() =>
            SaveChangesRetryHandler.SaveWithRetry(
                context,
                new RetryOptions { MaxRetries = 0, InitialDelay = TimeSpan.Zero },
                null,
                () => { }));
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private static void CreateSchemaOnDisk(string dbPath)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource={dbPath}")
            .Options;
        using var ctx = new TestDbContext(options);
        ctx.Database.EnsureCreated();
    }

    private static TransientFailureDbContext CreateFailingContextOnDisk(
        string dbPath, int failuresBeforeSuccess)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource={dbPath}")
            .Options;
        return new TransientFailureDbContext(options, failuresBeforeSuccess);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Best effort cleanup */ }
    }

    /// <summary>
    /// DbContext that throws "database is locked" DbUpdateException
    /// a specified number of times before delegating to the real SaveChanges.
    /// </summary>
    private class TransientFailureDbContext : TestDbContext
    {
        public int FailuresRemaining;

        public TransientFailureDbContext(
            DbContextOptions<TestDbContext> options, int failuresBeforeSuccess)
            : base(options)
        {
            FailuresRemaining = failuresBeforeSuccess;
        }

        public override int SaveChanges()
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new DbUpdateException(
                    "database is locked",
                    new Exception("database is locked"));
            }

            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new DbUpdateException(
                    "database is locked",
                    new Exception("database is locked"));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
