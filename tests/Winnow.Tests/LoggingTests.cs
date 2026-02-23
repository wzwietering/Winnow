using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Winnow.Tests;

public class LoggingTests : TestBase
{
    [Fact]
    public void Insert_logs_start_and_complete()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };

        saver.Insert(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Insert") && e.Message.Contains("starting"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Insert") && e.Message.Contains("completed"));
    }

    [Fact]
    public void Update_logs_start_and_complete()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = context.Products.ToList();
        products.ForEach(p => p.Price = 99.99m);
        context.ChangeTracker.Clear();

        saver.Update(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Update") && e.Message.Contains("starting"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public void Delete_logs_start_and_complete()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = context.Products.ToList();
        context.ChangeTracker.Clear();

        saver.Delete(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Delete") && e.Message.Contains("starting"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public void Upsert_logs_start_and_complete()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "New", Price = 10, Stock = 1 }
        };

        saver.Upsert(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Upsert") && e.Message.Contains("starting"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task InsertAsync_logs_start_and_complete()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };

        await saver.InsertAsync(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Insert") && e.Message.Contains("starting"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public void Failure_logs_warning_with_entity_info()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "Good", Price = 10, Stock = 1 },
            new() { Name = "Bad", Price = -1, Stock = 0 }
        };

        var result = saver.Insert(products);

        result.FailureCount.ShouldBe(1);
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("1 failed"));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("failed"));
    }

    [Fact]
    public void DivideAndConquer_logs_split_on_failure()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "Good1", Price = 10, Stock = 1 },
            new() { Name = "Bad", Price = -1, Stock = 0 },
            new() { Name = "Good2", Price = 20, Stock = 2 },
            new() { Name = "Good3", Price = 30, Stock = 3 }
        };

        saver.Insert(products, new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("splitting"));
    }

    [Fact]
    public void DivideAndConquer_logs_entity_failure()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "Bad", Price = -1, Stock = 0 }
        };

        var result = saver.Insert(products, new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.FailureCount.ShouldBe(1);
        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Product") &&
            e.Message.Contains("failed"));
    }

    [Fact]
    public void No_exception_without_logger()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 }
        };

        Should.NotThrow(() => saver.Insert(products));
    }

    [Fact]
    public void Completed_log_includes_success_and_failure_counts()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "Good", Price = 10, Stock = 1 },
            new() { Name = "Bad", Price = -1, Stock = 0 }
        };

        saver.Insert(products);

        var completedLog = logger.Entries.First(e => e.Message.Contains("completed"));
        completedLog.Message.ShouldContain("1 succeeded");
        completedLog.Message.ShouldContain("1 failed");
    }

    [Fact]
    public void Start_log_includes_entity_type_and_count()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 },
            new() { Name = "P2", Price = 20, Stock = 2 }
        };

        saver.Insert(products);

        var startLog = logger.Entries.First(e => e.Message.Contains("starting"));
        startLog.Message.ShouldContain("Product");
        startLog.Message.ShouldContain("2");
    }

    [Fact]
    public void Start_log_includes_strategy()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product> { new() { Name = "P1", Price = 10, Stock = 1 } };

        saver.Insert(products, new InsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        var startLog = logger.Entries.First(e => e.Message.Contains("starting"));
        startLog.Message.ShouldContain("DivideAndConquer");
    }

    [Fact]
    public void Completed_log_includes_round_trips()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 10, Stock = 1 },
            new() { Name = "P2", Price = 20, Stock = 2 }
        };

        saver.Insert(products);

        var completedLog = logger.Entries.First(e => e.Message.Contains("completed"));
        completedLog.Message.ShouldContain("2 round trips");
    }

    [Fact]
    public void Empty_batch_does_not_log()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product, int>(context, logger);

        saver.Insert(new List<Product>());

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Auto_detect_saver_with_logger()
    {
        using var context = CreateContext();
        var logger = new ListLogger();
        var saver = new Winnower<Product>(context, logger);
        var products = new List<Product> { new() { Name = "P1", Price = 10, Stock = 1 } };

        saver.Insert(products);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Insert"));
    }

    [Fact]
    public void Graph_operation_logs()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 1, 2);
        var logger = new ListLogger();
        var saver = new Winnower<CustomerOrder, int>(context, logger);

        var orders = context.CustomerOrders.ToList();
        context.ChangeTracker.Clear();

        saver.DeleteGraph(orders);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("DeleteGraph"));
    }
}
