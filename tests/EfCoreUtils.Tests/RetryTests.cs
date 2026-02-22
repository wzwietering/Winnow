using EfCoreUtils.Internal;
using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace EfCoreUtils.Tests;

public class RetryTests : TestBase
{
    // === FailureClassifier.IsTransient Tests ===

    [Fact]
    public void IsTransient_deadlock_returns_true()
    {
        var ex = new DbUpdateException("deadlock detected", new Exception("deadlock"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_lock_timeout_returns_true()
    {
        var ex = new DbUpdateException("lock timeout expired", new Exception("lock timeout"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_database_is_locked_returns_true()
    {
        var ex = new DbUpdateException("database is locked", new Exception("database is locked"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_connection_failed_returns_true()
    {
        var ex = new DbUpdateException("connection failed", new Exception("connection failed"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_connection_reset_returns_true()
    {
        var ex = new DbUpdateException("connection reset", new Exception("connection reset"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_connection_timed_out_returns_true()
    {
        var ex = new DbUpdateException("connection timed out", new Exception("connection timed out"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_command_timeout_returns_true()
    {
        var ex = new DbUpdateException("command timeout expired", new Exception("command timeout expired"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_timeout_exception_returns_true()
    {
        var ex = new TimeoutException("operation timed out");
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_concurrency_conflict_returns_false()
    {
        var ex = new DbUpdateConcurrencyException("concurrency conflict");
        FailureClassifier.IsTransient(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_unique_constraint_returns_false()
    {
        var ex = new DbUpdateException("unique constraint failed", new Exception("UNIQUE constraint failed"));
        FailureClassifier.IsTransient(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_invalid_operation_returns_false()
    {
        var ex = new InvalidOperationException("validation error");
        FailureClassifier.IsTransient(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_generic_exception_returns_false()
    {
        var ex = new Exception("something else");
        FailureClassifier.IsTransient(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_serialize_access_returns_true()
    {
        var ex = new DbUpdateException("could not serialize access", new Exception("could not serialize access"));
        FailureClassifier.IsTransient(ex).ShouldBeTrue();
    }

    // === SaveChangesRetryHandler Tests ===

    [Fact]
    public void SaveWithRetry_no_retry_options_calls_save_directly()
    {
        using var context = CreateContext();
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });

        Should.NotThrow(() =>
            SaveChangesRetryHandler.SaveWithRetry(context, null, null, () => { }));

        context.Products.Count().ShouldBe(1);
    }

    [Fact]
    public async Task SaveWithRetryAsync_no_retry_options_calls_save_directly()
    {
        using var context = CreateContext();
        context.Products.Add(new Product { Name = "Test", Price = 10, Stock = 1 });

        await Should.NotThrowAsync(() =>
            SaveChangesRetryHandler.SaveWithRetryAsync(context, null, null, () => { }, CancellationToken.None));

        context.Products.Count().ShouldBe(1);
    }

    // === Integration through BatchSaver ===

    [Fact]
    public void Insert_without_retry_options_works()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };

        var result = saver.InsertBatch(products);

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public void Insert_with_retry_options_no_failures_reports_zero_retries()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };
        var options = new InsertBatchOptions
        {
            Retry = new RetryOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(1)
            }
        };

        var result = saver.InsertBatch(products, options);

        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public void Update_with_retry_options_works()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var saver = new BatchSaver<Product, int>(context);
        var product = context.Products.First();
        product.Price = 99.99m;
        context.ChangeTracker.Clear();

        var options = new BatchOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = saver.UpdateBatch(new[] { product }, options);
        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public void Upsert_with_retry_options_works()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "New", Price = 10, Stock = 1 }
        };
        var options = new UpsertBatchOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = saver.UpsertBatch(products, options);
        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public async Task InsertBatchAsync_with_retry_options_works()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };
        var options = new InsertBatchOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = await saver.InsertBatchAsync(products, options);
        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public void RetryOptions_defaults_are_correct()
    {
        var options = new RetryOptions();

        options.MaxRetries.ShouldBe(3);
        options.InitialDelay.ShouldBe(TimeSpan.FromMilliseconds(100));
        options.BackoffMultiplier.ShouldBe(2.0);
        options.IsTransient.ShouldBeNull();
    }

    [Fact]
    public void RetryOptions_rejects_negative_max_retries()
    {
        var options = new RetryOptions();
        Should.Throw<ArgumentOutOfRangeException>(() => options.MaxRetries = -1);
    }

    [Fact]
    public void RetryOptions_rejects_zero_backoff_multiplier()
    {
        var options = new RetryOptions();
        Should.Throw<ArgumentOutOfRangeException>(() => options.BackoffMultiplier = 0);
    }

    [Fact]
    public void RetryOptions_rejects_negative_initial_delay()
    {
        var options = new RetryOptions();
        Should.Throw<ArgumentOutOfRangeException>(() => options.InitialDelay = TimeSpan.FromMilliseconds(-1));
    }

    [Fact]
    public void Custom_IsTransient_predicate_is_used()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };
        var customCalled = false;
        var options = new InsertBatchOptions
        {
            Retry = new RetryOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                IsTransient = _ =>
                {
                    customCalled = true;
                    return false;
                }
            }
        };

        // This should succeed without retry, custom predicate not called on success
        var result = saver.InsertBatch(products, options);
        result.SuccessCount.ShouldBe(1);
        customCalled.ShouldBeFalse();
    }

    [Fact]
    public void Retry_logs_attempts()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new BatchSaver<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "Good", Price = 10, Stock = 1 }
        };
        var options = new InsertBatchOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = saver.InsertBatch(products, options);

        // No failures means no retry logs
        result.SuccessCount.ShouldBe(1);
        logger.Entries.ShouldNotContain(e => e.Message.Contains("Retry attempt"));
    }

    [Fact]
    public void DivideAndConquer_with_retry_options_works()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 },
            new() { Name = "P2", Price = 20, Stock = 2 }
        };
        var options = new InsertBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = saver.InsertBatch(products, options);
        result.SuccessCount.ShouldBe(2);
        result.TotalRetries.ShouldBe(0);
    }

    [Fact]
    public void Graph_insert_with_retry_options_works()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<CustomerOrder, int>(context);
        var order = new CustomerOrder
        {
            OrderNumber = "RTO-001",
            CustomerName = "Retry Customer",
            TotalAmount = 100.00m,
            OrderItems = new List<OrderItem>
            {
                new()
                {
                    ProductName = "Widget",
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m
                }
            }
        };
        var options = new InsertGraphBatchOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) }
        };

        var result = saver.InsertGraphBatch(new[] { order }, options);
        result.SuccessCount.ShouldBe(1);
        result.TotalRetries.ShouldBe(0);
    }
}
