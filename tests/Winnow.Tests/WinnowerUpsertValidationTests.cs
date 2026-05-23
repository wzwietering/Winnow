using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertValidationTests : TestBase
{
    [Fact]
    public void Upsert_PreValidation_RejectsBeforeRouting()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var existing = context.Products.AsNoTracking().ToList();

        // Mix: existing entity (update), new entity (insert), invalid entity.
        var products = new[]
        {
            new Product { Id = existing[0].Id, Name = "u1", Price = 100m, Stock = 1, LastModified = DateTimeOffset.UtcNow, Version = existing[0].Version },
            new Product { Id = 0, Name = "new", Price = 50m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            new Product { Id = 0, Name = "bad", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new UpsertOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(products, options);

        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityIndex.ShouldBe(2);
        failure.Reason.ShouldBe(FailureReason.PreValidationError);
        // Default key path → AttemptedOperation = Insert.
        failure.AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
    }

    [Fact]
    public void Upsert_FailureBehaviorThrow_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Id = 0, Name = "bad", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new UpsertOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        });
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.Upsert(products, options));

        ex.Failures.Count.ShouldBe(1);
        ex.Failures[0].EntityIndex.ShouldBe(0);
    }

    [Fact]
    public void Upsert_PreValidationFailsExistingEntity_AttemptedOperationIsUpdate()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var existing = context.Products.AsNoTracking().First();

        var products = new[]
        {
            new Product { Id = existing.Id, Name = "u1", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new UpsertOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(products, options);

        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.AttemptedOperation.ShouldBe(UpsertOperationType.Update);
        failure.EntityId.ShouldBe(existing.Id);
    }

    [Fact]
    public void Upsert_AllInvalid_ZeroDatabaseRoundTrips()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Id = 0, Name = "a", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            new Product { Id = 0, Name = "b", Price = -2m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new UpsertOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(products, options);

        result.FailureCount.ShouldBe(2);
        result.DatabaseRoundTrips.ShouldBe(0);
    }

    [Fact]
    public void Upsert_EmptyBatch_NoFailuresNoRoundTrips()
    {
        using var context = CreateContext();
        var options = new UpsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(Array.Empty<Product>(), options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.DatabaseRoundTrips.ShouldBe(0);
    }
}
