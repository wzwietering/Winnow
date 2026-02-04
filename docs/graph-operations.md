# Graph Operations

Graph operations handle parent entities together with their children in a single batch operation. This is useful when you need to insert, update, or delete entity hierarchies while maintaining referential integrity.

## Insert Graph

Insert parent entities with their child collections in a single operation:

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = new List<CustomerOrder>
{
    new CustomerOrder
    {
        CustomerName = "Alice",
        OrderItems = new List<OrderItem>
        {
            new OrderItem { ProductId = 1, Quantity = 2 },
            new OrderItem { ProductId = 2, Quantity = 1 }
        }
    }
};

var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Parent and child IDs are populated after insert
foreach (var parentId in result.InsertedIds)
{
    var childIds = result.ChildIdsByParentId![parentId];
    Console.WriteLine($"Order {parentId} has items: {string.Join(", ", childIds)}");
}
```

## Update Graph

Update parent entities and their children, with control over what happens to removed children:

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = context.CustomerOrders
    .Include(o => o.OrderItems)
    .ToList();

// Modify parent and children
orders[0].Status = OrderStatus.Shipped;
orders[0].OrderItems[0].Quantity = 10;
orders[0].OrderItems.Add(new OrderItem { ... });
orders[0].OrderItems.Remove(orders[0].OrderItems.Last());

var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});
```

### Orphan Behavior

When children are removed from a collection during updates:

| Behavior | Effect |
|----------|--------|
| `Throw` (default) | Exception - prevents accidental data loss |
| `Delete` | Removed children are deleted from database |
| `Detach` | Removed children stay in database (may violate FK) |

## Delete Graph

Delete parent entities with their children:

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = context.CustomerOrders
    .Include(o => o.OrderItems)
    .Where(o => o.Status == OrderStatus.Cancelled)
    .ToList();

var result = saver.DeleteGraphBatch(orders, new DeleteGraphBatchOptions
{
    Strategy = BatchStrategy.OneByOne,
    CascadeBehavior = DeleteCascadeBehavior.Cascade
});
```

### Cascade Behavior

When deleting parent entities with children:

| Behavior | Effect |
|----------|--------|
| `Cascade` (default) | Delete children first, then parent. Always works. |
| `Throw` | Exception if parent has loaded children. |
| `ParentOnly` | Only delete parent, rely on database CASCADE DELETE. |

## Upsert Graph

Combine insert and update operations in a single graph batch:

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = new[]
{
    new CustomerOrder
    {
        Id = 0,  // New order (insert)
        CustomerName = "Alice",
        OrderItems = new List<OrderItem>
        {
            new() { Id = 0, ProductName = "Widget", Quantity = 5 },   // INSERT
            new() { Id = 123, ProductName = "Gadget", Quantity = 3 }  // UPDATE
        }
    }
};

var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete  // Required for graph updates
});

Console.WriteLine($"Inserted: {result.InsertedCount}");
Console.WriteLine($"Updated: {result.UpdatedCount}");
```

## Graph Hierarchy Results

For graph operations, results include the full hierarchy:

```csharp
// Access child IDs by parent ID
var childIds = result.ChildIdsByParentId![parentId];

// For upsert, get detailed hierarchy
var graphNode = result.GraphHierarchy![parentId];
Console.WriteLine($"Parent operation: {graphNode.Operation}");
foreach (var child in graphNode.Children)
{
    Console.WriteLine($"  Child {child.EntityId}: {child.Operation}");
}
```

## Common Options

| Option | Default | Description |
|--------|---------|-------------|
| `Strategy` | `OneByOne` | Batch strategy for the operation |
| `MaxDepth` | `10` | Maximum traversal depth |
| `IncludeReferences` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `false` | Include many-to-many navigations |
| `CircularReferenceHandling` | `Throw` | How to handle circular references |

See also:
- [Reference Navigation](reference-navigation.md)
- [Many-to-Many Relationships](many-to-many.md)
- [Self-Referencing Hierarchies](self-referencing.md)
