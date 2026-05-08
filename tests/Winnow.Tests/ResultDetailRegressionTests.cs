using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Internal;
using Winnow.Internal.Accumulators;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Regression tests for issues found during deep review of the ResultDetail
/// feature: parallel-orchestrator failure-detail propagation, accumulator
/// allocation gating, and merger short-circuiting.
/// </summary>
public class ResultDetailRegressionTests : ParallelTestBase
{
    // ---- C1 / I9: ParallelWinnower failure-result factories propagate ResultDetail ----

    [Fact]
    public async Task ParallelInsert_AtNone_FailureResultStampsCorrectDetail()
    {
        EnsureDatabaseCreated();
        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            callCount++;
            if (callCount <= 2) return CreateContextFactory()();
            throw new InvalidOperationException("simulated partition failure");
        };

        var saver = new ParallelWinnower<Product, int>(factory, 2);
        var products = BuildProducts(8);

        var result = await saver.InsertAsync(
            products, new InsertOptions { ResultDetail = ResultDetail.None });

        result.ResultDetail.ShouldBe(ResultDetail.None);
        result.FailureCount.ShouldBeGreaterThan(0);
        Should.Throw<InvalidOperationException>(() => result.Failures);
        Should.Throw<InvalidOperationException>(() => result.InsertedIds);
    }

    [Fact]
    public async Task ParallelUpdate_AtMinimal_FailureExceptionDropped()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            callCount++;
            if (callCount <= 2) return CreateContextFactory()();
            throw new InvalidOperationException("simulated partition failure");
        };

        var saver = new ParallelWinnower<Product, int>(factory, 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 1m;

        var result = await saver.UpdateAsync(
            products, new WinnowOptions { ResultDetail = ResultDetail.Minimal });

        result.ResultDetail.ShouldBe(ResultDetail.Minimal);
        result.FailureCount.ShouldBeGreaterThan(0);
        result.Failures.Count.ShouldBeGreaterThan(0);
        result.Failures.ShouldAllBe(f => f.Exception == null);
    }

    [Fact]
    public async Task ParallelUpsert_AtNone_FailureResultStampsCorrectDetail()
    {
        EnsureDatabaseCreated();
        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            callCount++;
            if (callCount <= 2) return CreateContextFactory()();
            throw new InvalidOperationException("simulated partition failure");
        };

        var saver = new ParallelWinnower<Product, int>(factory, 2);
        var products = BuildProducts(6);

        var result = await saver.UpsertAsync(
            products, new UpsertOptions { ResultDetail = ResultDetail.None });

        result.ResultDetail.ShouldBe(ResultDetail.None);
        result.FailureCount.ShouldBeGreaterThan(0);
        Should.Throw<InvalidOperationException>(() => result.Failures);
        Should.Throw<InvalidOperationException>(() => result.InsertedIds);
    }

    // ---- C2: UpsertAccumulator routes inserts vs updates correctly at None ----

    [Fact]
    public void Upsert_AtNone_InsertedAndUpdatedCountsAreAccurate()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var existing = context.Products.ToList();
        foreach (var p in existing) p.Stock = 555;
        var newProducts = BuildProducts(4);
        var combined = new List<Product>(existing);
        combined.AddRange(newProducts);

        var result = new Winnower<Product, int>(context).Upsert(
            combined, new UpsertOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(7);
        result.InsertedCount.ShouldBe(4);
        result.UpdatedCount.ShouldBe(3);
        result.ResultDetail.ShouldBe(ResultDetail.None);
    }

    [Fact]
    public void UpsertAccumulator_GetOperationDecision_RoundTripsValuesAcrossDenseIndices()
    {
        var accumulator = new UpsertAccumulator<int>(ResultDetail.None);

        for (var i = 0; i < 100; i++)
        {
            var op = i % 3 == 0 ? UpsertOperationType.Update : UpsertOperationType.Insert;
            accumulator.RecordOperationDecision(i, op);
        }

        for (var i = 0; i < 100; i++)
        {
            var expected = i % 3 == 0 ? UpsertOperationType.Update : UpsertOperationType.Insert;
            accumulator.GetOperationDecision(i).ShouldBe(expected);
        }
    }

    [Fact]
    public void UpsertAccumulator_GetOperationDecision_DefaultsToInsertForUnseenIndex()
    {
        var accumulator = new UpsertAccumulator<int>(ResultDetail.None);

        accumulator.GetOperationDecision(0).ShouldBe(UpsertOperationType.Insert);
        accumulator.GetOperationDecision(99).ShouldBe(UpsertOperationType.Insert);
        accumulator.WasInsertAttempt(99).ShouldBeTrue();
    }

    [Fact]
    public void UpsertAccumulator_RecordSuccessAsUpdate_FlipsDecisionEvenForUnseenIndex()
    {
        var accumulator = new UpsertAccumulator<int>(ResultDetail.None);

        accumulator.RecordSuccessAsUpdate(id: 1, index: 5, entity: new object());

        accumulator.GetOperationDecision(5).ShouldBe(UpsertOperationType.Update);
        accumulator.WasInsertAttempt(5).ShouldBeFalse();
    }

    // ---- C3: ResultMerger short-circuits remaps at None ----

    [Fact]
    public void MergeInsertResults_AtNone_PreservesDetailAndCounts()
    {
        var p1 = new InsertResult<int> { ResultDetail = ResultDetail.None, SuccessCount = 5, FailureCount = 0 };
        var p2 = new InsertResult<int> { ResultDetail = ResultDetail.None, SuccessCount = 3, FailureCount = 1 };

        var merged = ResultMerger.MergeInsertResults(
            [(p1, 0), (p2, 5)], TimeSpan.FromSeconds(1), 2, 0);

        merged.ResultDetail.ShouldBe(ResultDetail.None);
        merged.SuccessCount.ShouldBe(8);
        merged.FailureCount.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => merged.InsertedIds);
        Should.Throw<InvalidOperationException>(() => merged.Failures);
    }

    [Fact]
    public void MergeWinnowResults_AtMinimal_PreservesDetailAndIds()
    {
        var r1 = new WinnowResult<int>
        {
            ResultDetail = ResultDetail.Minimal,
            SuccessfulIds = [1, 2],
            SuccessCount = 2
        };
        var r2 = new WinnowResult<int>
        {
            ResultDetail = ResultDetail.Minimal,
            SuccessfulIds = [3],
            SuccessCount = 1
        };

        var merged = ResultMerger.MergeWinnowResults([r1, r2], TimeSpan.FromSeconds(1), 2, 0);

        merged.ResultDetail.ShouldBe(ResultDetail.Minimal);
        merged.SuccessfulIds.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void MergeUpsertResults_AtNone_SumsInsertedAndUpdatedCounts()
    {
        var p1 = new UpsertResult<int>
        {
            ResultDetail = ResultDetail.None,
            SuccessCount = 4, InsertedCount = 2, UpdatedCount = 2
        };
        var p2 = new UpsertResult<int>
        {
            ResultDetail = ResultDetail.None,
            SuccessCount = 3, InsertedCount = 1, UpdatedCount = 2
        };

        var merged = ResultMerger.MergeUpsertResults(
            [(p1, 0), (p2, 4)], TimeSpan.FromSeconds(1), 2, 0);

        merged.ResultDetail.ShouldBe(ResultDetail.None);
        merged.InsertedCount.ShouldBe(3);
        merged.UpdatedCount.ShouldBe(4);
        Should.Throw<InvalidOperationException>(() => merged.InsertedIds);
    }

    // ---- I6: WinnowResult / UpsertResult per-entity throw-on-access at None ----

    [Fact]
    public void Update_AtNone_PerEntityAccessorsThrow()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.ToList();
        foreach (var p in products) p.Stock += 10;

        var result = new Winnower<Product, int>(context).Update(
            products, new WinnowOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(3);
        Should.Throw<InvalidOperationException>(() => result.SuccessfulIds);
        Should.Throw<InvalidOperationException>(() => result.Failures);
        Should.Throw<InvalidOperationException>(() => result.FailedIds);
    }

    [Fact]
    public void Delete_AtNone_PerEntityAccessorsThrow()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var products = context.Products.ToList();

        var result = new Winnower<Product, int>(context).Delete(
            products, new DeleteOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.SuccessfulIds);
        Should.Throw<InvalidOperationException>(() => result.Failures);
        Should.Throw<InvalidOperationException>(() => result.FailedIds);
    }

    [Fact]
    public void Upsert_AtNone_AllPerEntityAccessorsThrow()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var existing = context.Products.First();
        existing.Stock = 99;
        var newOne = BuildProducts(1).Single();

        var result = new Winnower<Product, int>(context).Upsert(
            [existing, newOne], new UpsertOptions { ResultDetail = ResultDetail.None });

        result.SuccessCount.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => result.InsertedIds);
        Should.Throw<InvalidOperationException>(() => result.UpdatedIds);
        Should.Throw<InvalidOperationException>(() => result.SuccessfulIds);
        Should.Throw<InvalidOperationException>(() => result.AllUpsertedEntities);
        Should.Throw<InvalidOperationException>(() => result.GetByIndex(0));
        Should.Throw<InvalidOperationException>(() => result.GetFailureByIndex(0));
    }

    // ---- I5: Failure paths at None for Insert and Upsert ----

    [Fact]
    public void Insert_AtNone_FailureCountedButNotRecorded()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var existingId = context.Products.First().Id;

        var conflict = new Product
        {
            Id = existingId,
            Name = "Duplicate",
            Price = 1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        };

        var result = new Winnower<Product, int>(context).Insert(
            [conflict],
            new InsertOptions { Strategy = BatchStrategy.OneByOne, ResultDetail = ResultDetail.None });

        result.FailureCount.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => result.Failures);
    }

    [Fact]
    public void Upsert_FailureAtMinimal_ExceptionDroppedButFailureCountAccurate()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var bogus = new Product
        {
            Id = 9_999_999,
            Name = "BogusUpdate",
            Price = 1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        };

        var result = new Winnower<Product, int>(context).Upsert(
            [bogus], new UpsertOptions { ResultDetail = ResultDetail.Minimal });

        // Bogus key triggers update path that fails because no row matches.
        result.FailureCount.ShouldBe(1);
        result.Failures.Count.ShouldBe(1);
        result.Failures[0].Exception.ShouldBeNull();
        result.Failures[0].ErrorMessage.ShouldNotBeNullOrEmpty();
    }

    private static List<Product> BuildProducts(int count) => Enumerable.Range(1, count)
        .Select(i => new Product
        {
            Name = $"Product {Guid.NewGuid():N}",
            Price = 10m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();
}
