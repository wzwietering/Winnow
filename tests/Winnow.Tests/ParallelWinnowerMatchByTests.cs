using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class ParallelWinnowerMatchByTests : ParallelTestBase
{
    [Fact]
    public async Task UpsertAsync_MatchBy_NullKeyedEntitiesAcrossPartitions_MergedCountIsSum()
    {
        // Regression for: ResultMerger.MergeUpsertResults did not propagate
        // InsertedWithNullMatchKeyCount, so callers using ParallelWinnower + MatchBy
        // silently received 0 even when null-keyed entities were inserted.
        EnsureDatabaseCreated();

        var products = new[]
        {
            NewProductWithNullCategory("Orphan-A"),
            NewProductWithNullCategory("Orphan-B"),
            NewProductWithNullCategory("Orphan-C"),
            NewProductWithNullCategory("Orphan-D"),
        };

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var result = await saver.UpsertAsync(
            products,
            new UpsertOptions().WithMatchBy<Product, int?>(p => p.CategoryId));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(4);
        result.InsertedWithNullMatchKeyCount.ShouldBe(4,
            "ResultMerger must sum InsertedWithNullMatchKeyCount across partitions; " +
            "otherwise callers using ParallelWinnower + MatchBy lose the data-quality signal.");
    }

    private static Product NewProductWithNullCategory(string name) => new()
    {
        Id = 0,
        Name = name,
        Price = 1m,
        Stock = 1,
        LastModified = DateTimeOffset.UtcNow,
        CategoryId = null
    };
}
