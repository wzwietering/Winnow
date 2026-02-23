using Winnow;
using Winnow.Tests.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests.CompositeKeyIntegration;

public class CompositeKeyGraphTests : CompositeKeyTestBase
{
    [Fact]
    public void InsertGraph_CompositeParent_WithChildren_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraph([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        loaded.Notes.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraph_CompositeKey_ModifyChild_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        var noteToModify = loaded.Notes.First();
        noteToModify.Note = "Modified Note";

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.UpdateGraph([loaded]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var reloaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        reloaded.Notes.ShouldContain(n => n.Note == "Modified Note");
    }

    [Fact]
    public void UpdateGraph_CompositeKey_OrphanDetection_Works()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        var noteToRemove = loaded.Notes.First();
        loaded.Notes.Remove(noteToRemove);

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.UpdateGraph([loaded], new GraphOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var reloaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        reloaded.Notes.Count.ShouldBe(1);
    }

    [Fact]
    public void DeleteGraph_CompositeKey_CascadeDeletes()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);
        context.ChangeTracker.Clear();

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.DeleteGraph([loaded]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
        context.OrderLineNotes.Count(n => n.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void GraphHierarchy_TracksCompositeKeys()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraph([orderLine]);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(1);

        var parentKey = new CompositeKey(orderId, 1);
        var parentNode = result.GraphHierarchy.First(n => n.EntityId.Equals(parentKey));
        parentNode.GetChildIds().Count.ShouldBe(2);
    }

    [Fact]
    public void MaxDepth_RespectsCompositeKeyHierarchy()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Note 1", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraph([orderLine], new InsertGraphOptions
        {
            MaxDepth = 0 // Only insert parent, not children
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(1);
        context.OrderLineNotes.Count(n => n.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public void MultiLevel_CompositeAndSimpleKeys_Mixed()
    {
        using var context = CreateContext();

        // CustomerOrder (simple key) -> OrderLine (composite key) -> OrderLineNote (simple key)
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-MIX-001",
            CustomerName = "Mixed Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Multi-level test note", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraph([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loadedNote = context.OrderLineNotes
            .First(n => n.OrderId == orderId && n.LineNumber == 1);
        loadedNote.Id.ShouldBeGreaterThan(0);
    }
}
