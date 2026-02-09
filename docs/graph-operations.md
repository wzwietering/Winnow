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

## Navigation Filtering

Control which navigation properties are traversed during graph operations. This is useful when you want to operate on part of an entity graph without affecting the rest.

### Include Mode (Allowlist)

Only explicitly listed navigations are traversed. Entity types without rules have NO navigations traversed.

```csharp
// Only traverse OrderItems, skip Reservations (and any other navigations)
var filter = NavigationFilter.Include()
    .Navigation<CustomerOrder>(o => o.OrderItems);

var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
{
    NavigationFilter = filter
});
// Inserts orders and their items, but NOT item reservations
```

### Exclude Mode (Blocklist)

Listed navigations are skipped, all others are traversed normally.

```csharp
// Traverse everything EXCEPT Reservations
var filter = NavigationFilter.Exclude()
    .Navigation<OrderItem>(i => i.Reservations);

var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
{
    NavigationFilter = filter
});
// Inserts orders and items, but NOT reservations
```

### Filter with Orphan Detection

Navigation filtering is consistent across all phases of a graph operation. When a navigation is filtered out, orphan detection ignores it too:

```csharp
var filter = NavigationFilter.Include()
    .Navigation<CustomerOrder>(o => o.OrderItems);

// Removing reservations won't trigger orphan detection since they're filtered out
orders[0].OrderItems.First().Reservations.Clear();

var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Throw,
    NavigationFilter = filter
});
```

### Combining Filter with MaxDepth

`NavigationFilter` and `MaxDepth` work together. Both constraints are applied:

```csharp
var filter = NavigationFilter.Include()
    .Navigation<CustomerOrder>(o => o.OrderItems)
    .Navigation<OrderItem>(i => i.Reservations);

var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
{
    NavigationFilter = filter,
    MaxDepth = 1  // Limits to depth 1, so reservations at depth 2 are not reached
});
```

### Understanding Filter and Flag Interaction

For a navigation to be traversed, both conditions must be true:
1. The navigation type must be enabled via flags (`IncludeReferences`, `IncludeManyToMany`)
2. The navigation must pass the filter rules (if a filter is specified)

Think of flags as "gates" and filters as "allow/block lists":

| Scenario | Flag | Filter | Result |
|----------|------|--------|--------|
| Collection navigation | (always on) | Included / no rule | Traversed |
| Collection navigation | (always on) | Excluded | Blocked by filter |
| Reference navigation | `IncludeReferences = true` | Included in filter | Traversed |
| Reference navigation | `IncludeReferences = false` | Included in filter | **Throws** (conflict) |
| M2M navigation | `IncludeManyToMany = true` | Excluded in filter | Blocked by filter |

### Flag Conflict Validation

If a filter includes a reference navigation but `IncludeReferences = false`, or a many-to-many navigation but `IncludeManyToMany = false`, an `InvalidOperationException` is thrown at operation start.

> **Note:** This validation only applies to **Include mode** filters. Exclude mode
> filters do not trigger flag conflict validation because excluding a navigation that wouldn't
> be traversed anyway is redundant but not incorrect.

### Navigation Name Validation

Both include and exclude mode filters validate that each navigation name actually exists in the EF model. If a filter references a non-existent navigation property, an `InvalidOperationException` is thrown. This catches typos and refactoring errors early.

## Common Options

| Option | Default | Description |
|--------|---------|-------------|
| `Strategy` | `OneByOne` | Batch strategy for the operation |
| `MaxDepth` | `10` | Maximum traversal depth |
| `NavigationFilter` | `null` | Filter which navigations are traversed (see above) |
| `IncludeReferences` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `false` | Include many-to-many navigations |
| `CircularReferenceHandling` | `Throw` | How to handle circular references |

### Future Consideration: Filter-Only Mode

In a future major version, `NavigationFilter` could become the single source of truth for navigation traversal control, replacing the boolean `IncludeReferences` and `IncludeManyToMany` flags. This would simplify the API by removing the "flag + filter" interaction and making the filter the only mechanism for specifying traversal rules. No implementation changes are needed now.

See also:
- [Reference Navigation](reference-navigation.md)
- [Many-to-Many Relationships](many-to-many.md)
- [Self-Referencing Hierarchies](self-referencing.md)
