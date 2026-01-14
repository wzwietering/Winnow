# EfCoreUtils

Efficient batch update utilities for Entity Framework Core with failure isolation and tracking.

## Features

- **Batch Updates**: Update many entities while tracking success/failure per entity
- **Graph Updates**: Update parent + children together with orphan handling
- **Two Strategies**: OneByOne (predictable) vs DivideAndConquer (optimized for low failure rates)
- **Detailed Results**: Track successful IDs, failures with reasons, round trips, and duration

## Quick Start

```csharp
var saver = new BatchSaver<Product>(context);

// Parent-only updates
var result = saver.UpdateBatch(products);

// Graph updates (parent + children)
var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer,
    OrphanedChildBehavior = OrphanBehavior.Delete
});

// Check results
Console.WriteLine($"Saved: {result.SuccessCount}, Failed: {result.FailureCount}");
foreach (var failure in result.Failures)
{
    Console.WriteLine($"  {failure.EntityId}: {failure.ErrorMessage}");
}
```

## Strategies

| Strategy | Best For | Round Trips |
|----------|----------|-------------|
| **OneByOne** | High failure rates, simplicity | N (predictable) |
| **DivideAndConquer** | Low failure rates (<5%) | 1 at 0%, ~150 at 1% |

### Performance (1000 entities, SQLite)

| Failure Rate | OneByOne Trips | D&C Trips | D&C Speedup |
|--------------|----------------|-----------|-------------|
| 0%           | 1000           | 1         | 1.4x faster |
| 1%           | 1000           | 147       | 1.4x faster |
| 5%           | 1000           | 523       | 0.6x (slower) |
| 25%+         | 1000           | 1499+     | Use OneByOne |

**Note**: D&C reduces round trips dramatically but has overhead. With network databases (SQL Server, PostgreSQL), D&C wins more clearly due to latency savings.

## Graph Updates

Update parent entities with their children in a single operation:

```csharp
var orders = context.CustomerOrders
    .Include(o => o.OrderItems)
    .ToList();

// Modify parent and children
orders[0].Status = OrderStatus.Shipped;
orders[0].OrderItems[0].Quantity = 10;

// Add new child
orders[0].OrderItems.Add(new OrderItem { ... });

// Remove child (with explicit orphan handling)
orders[0].OrderItems.Remove(orders[0].OrderItems.Last());

var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete // or Throw (default), Detach
});
```

### Orphan Behavior

When children are removed from a collection:

| Behavior | Effect |
|----------|--------|
| `Throw` (default) | Exception - prevents accidental data loss |
| `Delete` | Removed children are deleted from database |
| `Detach` | Removed children stay in database (may violate FK) |

### Graph Failure Isolation

Each graph (parent + children) succeeds or fails as a unit:

```
Batch: [Order1 + items, Order2 + items, Order3 + items]
       ✅ saved        ❌ failed         ✅ saved

Order2's failure doesn't affect Order1 or Order3.
```

## BatchResult

```csharp
result.SuccessfulIds      // IDs that saved successfully
result.Failures           // List<BatchFailure> with EntityId, ErrorMessage, Reason
result.ChildIdsByParentId // For graph updates: parent ID → child IDs
result.DatabaseRoundTrips // Actual DB calls made
result.Duration           // Total time
result.IsCompleteSuccess  // All succeeded
result.IsPartialSuccess   // Some succeeded, some failed
result.IsCompleteFailure  // All failed
```

## When to Use What

| Scenario | Method | Strategy |
|----------|--------|----------|
| Parent properties only | `UpdateBatch` | DivideAndConquer (if <5% failures) |
| Need to update children | `UpdateGraphBatch` | DivideAndConquer |
| High failure rate (>25%) | Either | OneByOne |
| Adding/removing children | `UpdateGraphBatch` | Set OrphanBehavior explicitly |
