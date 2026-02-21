using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace EfCoreUtils.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"));
        services.AddEfCoreUtils<TestDbContext>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Resolves_typed_batch_saver()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var saver = scope.ServiceProvider.GetService<IBatchSaver<Product, int>>();

        saver.ShouldNotBeNull();
        saver.ShouldBeOfType<BatchSaver<Product, int>>();
    }

    [Fact]
    public void Resolves_auto_detect_batch_saver()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var saver = scope.ServiceProvider.GetService<IBatchSaver<Product>>();

        saver.ShouldNotBeNull();
        saver.ShouldBeOfType<BatchSaver<Product>>();
    }

    [Fact]
    public void Resolves_scoped_lifetime()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var saver1 = scope.ServiceProvider.GetRequiredService<IBatchSaver<Product, int>>();
        var saver2 = scope.ServiceProvider.GetRequiredService<IBatchSaver<Product, int>>();

        saver1.ShouldBeSameAs(saver2);
    }

    [Fact]
    public void Different_scopes_get_different_instances()
    {
        using var provider = BuildProvider();

        IBatchSaver<Product, int> saver1;
        IBatchSaver<Product, int> saver2;

        using (var scope1 = provider.CreateScope())
        {
            saver1 = scope1.ServiceProvider.GetRequiredService<IBatchSaver<Product, int>>();
        }

        using (var scope2 = provider.CreateScope())
        {
            saver2 = scope2.ServiceProvider.GetRequiredService<IBatchSaver<Product, int>>();
        }

        saver1.ShouldNotBeSameAs(saver2);
    }

    [Fact]
    public void Resolves_multiple_entity_types()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var productSaver = scope.ServiceProvider.GetService<IBatchSaver<Product, int>>();
        var orderSaver = scope.ServiceProvider.GetService<IBatchSaver<CustomerOrder, int>>();

        productSaver.ShouldNotBeNull();
        orderSaver.ShouldNotBeNull();
    }

    [Fact]
    public void Idempotent_registration()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"));
        services.AddEfCoreUtils<TestDbContext>();
        services.AddEfCoreUtils<TestDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var saver = scope.ServiceProvider.GetService<IBatchSaver<Product, int>>();
        saver.ShouldNotBeNull();
    }

    [Fact]
    public void End_to_end_insert()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var saver = scope.ServiceProvider.GetRequiredService<IBatchSaver<Product, int>>();

        var products = new List<Product>
        {
            new() { Name = "DI Product 1", Price = 10.00m, Stock = 5 },
            new() { Name = "DI Product 2", Price = 20.00m, Stock = 10 }
        };

        var result = saver.InsertBatch(products);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public void Works_with_configure_action()
    {
        var configured = false;
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"));
        services.AddEfCoreUtils<TestDbContext>(_ => configured = true);

        configured.ShouldBeTrue();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var saver = scope.ServiceProvider.GetService<IBatchSaver<Product, int>>();
        saver.ShouldNotBeNull();
    }

    [Fact]
    public void Resolves_composite_key_auto_detect()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var saver = scope.ServiceProvider.GetService<IBatchSaver<OrderLine>>();

        saver.ShouldNotBeNull();
        saver.IsCompositeKey.ShouldBeTrue();
    }

    [Fact]
    public void DbContext_base_type_bridges_to_concrete()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetService<DbContext>();
        var testContext = scope.ServiceProvider.GetService<TestDbContext>();

        dbContext.ShouldNotBeNull();
        testContext.ShouldNotBeNull();
        dbContext.ShouldBeSameAs(testContext);
    }
}
