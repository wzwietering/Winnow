using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;

namespace Winnow.Tests.Infrastructure;

/// <summary>
/// Shared helpers for MatchBy upsert tests. Centralises duplicated seed-and-conflict
/// patterns so a schema change ripples through one definition.
/// </summary>
internal static class MatchByTestHelpers
{
    internal static CustomerOrder SeedOrder(
        TestDbContext context, string orderNumber, string customerName, decimal total)
    {
        var order = new CustomerOrder
        {
            OrderNumber = orderNumber,
            CustomerId = 1,
            CustomerName = customerName,
            TotalAmount = total,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return order;
    }

    /// <summary>
    /// Inserts a conflicting CustomerOrder row exactly once via <see cref="DbContext.SavingChanges"/>,
    /// modelling another client racing in between Winnow's pre-SELECT and INSERT.
    /// </summary>
    internal static void InjectConflictingRowOnce(
        TestDbContext context, string orderNumber, string customerName)
    {
        var fired = false;
        context.SavingChanges += (_, _) =>
        {
            if (fired) return;
            fired = true;
            var rowsAffected = context.Database.ExecuteSqlInterpolated(
                $@"INSERT INTO CustomerOrders (OrderNumber, CustomerId, CustomerName, Status, TotalAmount, OrderDate, Version)
                   VALUES ({orderNumber}, 1, {customerName}, 0, 1.00, '2020-01-01 00:00:00', X'0000000000000001')");
            rowsAffected.ShouldBe(1);
        };
    }
}
