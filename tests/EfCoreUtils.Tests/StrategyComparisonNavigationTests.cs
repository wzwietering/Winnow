using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class StrategyComparisonNavigationTests : TestBase
{
    [Fact]
    public void CompareStrategies_WithNavigation_0PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(1000, 0, "0% Failure (with navigation)");

        divideConquer.DatabaseRoundTrips.ShouldBeLessThan(oneByOne.DatabaseRoundTrips);
        oneByOne.SuccessCount.ShouldBe(1000);
        divideConquer.SuccessCount.ShouldBe(1000);
    }

    [Fact]
    public void CompareStrategies_WithNavigation_1PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(1000, 10, "1% Failure (with navigation)");

        divideConquer.DatabaseRoundTrips.ShouldBeLessThan(oneByOne.DatabaseRoundTrips);
        oneByOne.SuccessCount.ShouldBe(990);
        divideConquer.SuccessCount.ShouldBe(990);
    }

    [Fact]
    public void CompareStrategies_WithNavigation_5PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(1000, 50, "5% Failure (with navigation)");

        oneByOne.SuccessCount.ShouldBe(950);
        divideConquer.SuccessCount.ShouldBe(950);
    }

    [Fact]
    public void CompareStrategies_WithNavigation_25PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(1000, 250, "25% Failure (with navigation)");

        oneByOne.SuccessCount.ShouldBe(750);
        divideConquer.SuccessCount.ShouldBe(750);
    }

    [Fact]
    public void CompareStrategies_WithNavigation_50PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(1000, 500, "50% Failure (with navigation)");

        oneByOne.SuccessCount.ShouldBe(500);
        divideConquer.SuccessCount.ShouldBe(500);
    }

    [Fact]
    public void CompareStrategies_WithNavigation_100PercentFailure()
    {
        var (oneByOne, divideConquer) = CompareStrategies(100, 100, "100% Failure (with navigation)");

        oneByOne.FailureCount.ShouldBe(100);
        divideConquer.FailureCount.ShouldBe(100);
        divideConquer.DatabaseRoundTrips.ShouldBeGreaterThan(oneByOne.DatabaseRoundTrips);
    }

    private (BatchResult OneByOne, BatchResult DivideAndConquer) CompareStrategies(
        int orderCount,
        int invalidCount,
        string scenarioName)
    {
        var builder = new TestDataBuilder();

        // Test OneByOne strategy
        using var context1 = CreateContext();
        SeedCustomerOrders(context1, orderCount, itemsPerOrder: 3);

        var ordersForOneByOne = context1.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(orderCount)
            .ToList();

        // Mark first N orders as invalid (negative TotalAmount)
        for (int i = 0; i < invalidCount && i < ordersForOneByOne.Count; i++)
        {
            ordersForOneByOne[i].TotalAmount = -100.00m;
        }

        // Also update valid orders
        for (int i = invalidCount; i < ordersForOneByOne.Count; i++)
        {
            ordersForOneByOne[i].TotalAmount += 10.00m;
        }

        var oneByOneResult = RunWithStrategy(context1, ordersForOneByOne, BatchStrategy.OneByOne);

        // Test DivideAndConquer with fresh context
        using var context2 = CreateContext();
        SeedCustomerOrders(context2, orderCount, itemsPerOrder: 3);

        var ordersForDivideConquer = context2.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(orderCount)
            .ToList();

        // Mark first N orders as invalid (negative TotalAmount)
        for (int i = 0; i < invalidCount && i < ordersForDivideConquer.Count; i++)
        {
            ordersForDivideConquer[i].TotalAmount = -100.00m;
        }

        // Also update valid orders
        for (int i = invalidCount; i < ordersForDivideConquer.Count; i++)
        {
            ordersForDivideConquer[i].TotalAmount += 10.00m;
        }

        var divideConquerResult = RunWithStrategy(context2, ordersForDivideConquer, BatchStrategy.DivideAndConquer);

        Console.WriteLine($"{scenarioName}:");
        Console.WriteLine($"  OneByOne: {oneByOneResult.Duration.TotalMilliseconds}ms, {oneByOneResult.DatabaseRoundTrips} round trips");
        Console.WriteLine($"  DivideAndConquer: {divideConquerResult.Duration.TotalMilliseconds}ms, {divideConquerResult.DatabaseRoundTrips} round trips");
        if (divideConquerResult.Duration.TotalMilliseconds > 0)
        {
            Console.WriteLine($"  Ratio: {oneByOneResult.Duration.TotalMilliseconds / divideConquerResult.Duration.TotalMilliseconds:F2}x");
        }

        return (oneByOneResult, divideConquerResult);
    }

    private BatchResult RunWithStrategy(TestDbContext context, List<CustomerOrder> orders, BatchStrategy strategy)
    {
        var options = new BatchOptions
        {
            Strategy = strategy,
            ValidateNavigationProperties = false
        };
        var saver = new BatchSaver<CustomerOrder>(context);
        return saver.UpdateBatch(orders, options);
    }
}
