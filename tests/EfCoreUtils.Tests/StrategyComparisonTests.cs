using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Shouldly;

namespace EfCoreUtils.Tests;

public class StrategyComparisonTests : TestBase
{
    [Fact]
    public void CompareStrategies_With0PercentFailure_DivideAndConquerShouldWin()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price += 1.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(1000).ToList();
        foreach (var product in productsToUpdate2)
        {
            product.Price += 1.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"0% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Speedup: {oneByOneResult.Duration.TotalMilliseconds / divideAndConquerResult.Duration.TotalMilliseconds:F2}x");

        divideAndConquerResult.DatabaseRoundTrips.ShouldBeLessThan(oneByOneResult.DatabaseRoundTrips);
        // Duration comparison can be flaky due to system load, so we just verify round trips
        // In practice, divide-and-conquer should be faster, but timing assertions are unreliable in tests
    }

    [Fact]
    public void CompareStrategies_With1PercentFailure_DivideAndConquerShouldWin()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate.Count; i++)
        {
            if (i % 100 == 0)
                productsToUpdate[i].Price = -10.00m;
            else
                productsToUpdate[i].Price += 1.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate2.Count; i++)
        {
            if (i % 100 == 0)
                productsToUpdate2[i].Price = -10.00m;
            else
                productsToUpdate2[i].Price += 1.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"1% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Speedup: {oneByOneResult.Duration.TotalMilliseconds / divideAndConquerResult.Duration.TotalMilliseconds:F2}x");

        divideAndConquerResult.DatabaseRoundTrips.ShouldBeLessThan(oneByOneResult.DatabaseRoundTrips);
    }

    [Fact]
    public void CompareStrategies_With5PercentFailure_MeasuresPerformance()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate.Count; i++)
        {
            if (i % 20 == 0)
                productsToUpdate[i].Price = -10.00m;
            else
                productsToUpdate[i].Price += 1.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate2.Count; i++)
        {
            if (i % 20 == 0)
                productsToUpdate2[i].Price = -10.00m;
            else
                productsToUpdate2[i].Price += 1.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"5% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Speedup: {oneByOneResult.Duration.TotalMilliseconds / divideAndConquerResult.Duration.TotalMilliseconds:F2}x");

        oneByOneResult.SuccessCount.ShouldBe(950);
        divideAndConquerResult.SuccessCount.ShouldBe(950);
    }

    [Fact]
    public void CompareStrategies_With25PercentFailure_MeasuresPerformance()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate.Count; i++)
        {
            if (i % 4 == 0)
                productsToUpdate[i].Price = -10.00m;
            else
                productsToUpdate[i].Price += 1.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate2.Count; i++)
        {
            if (i % 4 == 0)
                productsToUpdate2[i].Price = -10.00m;
            else
                productsToUpdate2[i].Price += 1.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"25% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Ratio: {oneByOneResult.Duration.TotalMilliseconds / divideAndConquerResult.Duration.TotalMilliseconds:F2}x");

        oneByOneResult.SuccessCount.ShouldBe(750);
        divideAndConquerResult.SuccessCount.ShouldBe(750);
    }

    [Fact]
    public void CompareStrategies_With50PercentFailure_MeasuresPerformance()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate.Count; i++)
        {
            if (i % 2 == 0)
                productsToUpdate[i].Price = -10.00m;
            else
                productsToUpdate[i].Price += 1.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(1000).ToList();
        for (int i = 0; i < productsToUpdate2.Count; i++)
        {
            if (i % 2 == 0)
                productsToUpdate2[i].Price = -10.00m;
            else
                productsToUpdate2[i].Price += 1.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"50% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Ratio: {oneByOneResult.Duration.TotalMilliseconds / divideAndConquerResult.Duration.TotalMilliseconds:F2}x");

        oneByOneResult.SuccessCount.ShouldBe(500);
        divideAndConquerResult.SuccessCount.ShouldBe(500);
    }

    [Fact]
    public void CompareStrategies_With100PercentFailure_OneByOneShouldWin()
    {
        using var context = CreateContext();
        SeedData(context, 100);

        var productsToUpdate = context.Products.Take(100).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price = -10.00m;
        }

        var oneByOneResult = RunWithStrategy(context, productsToUpdate, BatchStrategy.OneByOne);

        context.ChangeTracker.Clear();
        var productsToUpdate2 = context.Products.Take(100).ToList();
        foreach (var product in productsToUpdate2)
        {
            product.Price = -10.00m;
        }
        var divideAndConquerResult = RunWithStrategy(context, productsToUpdate2, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"100% Failure Rate:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideAndConquerResult.Duration.TotalMilliseconds}ms, {divideAndConquerResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  Ratio: {divideAndConquerResult.Duration.TotalMilliseconds / oneByOneResult.Duration.TotalMilliseconds:F2}x slower");

        oneByOneResult.FailureCount.ShouldBe(100);
        divideAndConquerResult.FailureCount.ShouldBe(100);
        oneByOneResult.DatabaseRoundTrips.ShouldBe(100);
        divideAndConquerResult.DatabaseRoundTrips.ShouldBeGreaterThan(oneByOneResult.DatabaseRoundTrips);
    }

    private BatchResult RunWithStrategy(TestDbContext context, List<Product> products, BatchStrategy strategy)
    {
        var options = new BatchOptions { Strategy = strategy };
        var saver = new BatchSaver<Product>(context);
        return saver.UpdateBatch(products, options);
    }
}
