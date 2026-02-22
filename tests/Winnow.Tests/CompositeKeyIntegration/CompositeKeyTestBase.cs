using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests.CompositeKeyIntegration;

public abstract class CompositeKeyTestBase : TestBase
{
    protected static int CreateCustomerOrder(TestDbContext context)
    {
        var order = new CustomerOrder
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();
        return orderId;
    }

    protected static void InsertOrderLines(TestDbContext context, int orderId, int count)
    {
        var orderLines = Enumerable.Range(1, count).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i,
            UnitPrice = 10.00m + i
        }).ToList();

        context.OrderLines.AddRange(orderLines);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    protected static void InsertInventoryLocations(TestDbContext context, int count)
    {
        var locations = Enumerable.Range(1, count).Select(i => new InventoryLocation
        {
            WarehouseCode = $"WH{i:D2}",
            AisleNumber = i,
            BinCode = $"BIN-{i:D2}",
            Quantity = i * 10,
            LastUpdated = DateTime.UtcNow
        }).ToList();

        context.InventoryLocations.AddRange(locations);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    protected static OrderLine InsertOrderLineWithNotes(
        TestDbContext context, int orderId, int lineNumber, int noteCount)
    {
        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = lineNumber,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes = Enumerable.Range(1, noteCount).Select(i => new OrderLineNote
            {
                Note = $"Note {i}",
                CreatedAt = DateTime.UtcNow
            }).ToList()
        };

        context.OrderLines.Add(orderLine);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return orderLine;
    }
}
