# Upsert Operations

Upsert operations perform INSERT or UPDATE based on key detection:
- **Default key** (0, Guid.Empty, null) → INSERT
- **Non-default key** → UPDATE

## Race Condition Warning

**This is NOT a database-level MERGE operation.** There is a time gap between key detection and `SaveChanges()`. If another process inserts a row with the same key during this window, you may encounter:

- Duplicate key violations (for inserts that should have been updates)
- Concurrency exceptions (for updates to non-existent rows)

**Mitigation strategies:**

1. **DuplicateKeyStrategy.RetryAsUpdate**: Built-in retry that converts failed INSERTs to UPDATEs
2. **Database-level upsert**: For high-concurrency scenarios, use raw SQL with `MERGE` (SQL Server) or `ON CONFLICT` (PostgreSQL)
3. **Optimistic concurrency**: Add a `RowVersion` column and handle `DbUpdateConcurrencyException`
4. **Application-level locking**: Use distributed locks for critical sections

## DuplicateKeyStrategy

Handle race conditions automatically with `DuplicateKeyStrategy`:

```csharp
var result = saver.UpsertBatch(products, new UpsertBatchOptions
{
    DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate
});
```

| Strategy | Behavior |
|----------|----------|
| `Fail` (default) | Record duplicate key errors in Failures collection |
| `RetryAsUpdate` | Retry failed INSERT as UPDATE (handles race conditions) |
| `Skip` | Skip silently without recording as failure |

## Basic Upsert

```csharp
var saver = new BatchSaver<Product, int>(context);

var products = new[]
{
    new Product { Id = 0, Name = "New Product", Price = 9.99m },     // INSERT (Id=0)
    new Product { Id = 42, Name = "Updated Name", Price = 19.99m }, // UPDATE (Id=42)
    new Product { Id = 0, Name = "Another New", Price = 5.00m }     // INSERT (Id=0)
};

var result = saver.UpsertBatch(products);

Console.WriteLine($"Inserted: {result.InsertedCount}");  // 2
Console.WriteLine($"Updated: {result.UpdatedCount}");    // 1

// Access results by operation type
foreach (var entity in result.InsertedEntities)
{
    Console.WriteLine($"Inserted product: {entity.Id}");
}
foreach (var entity in result.UpdatedEntities)
{
    Console.WriteLine($"Updated product: {entity.Id}");
}
```

## Graph Upsert

Combine insert and update operations for parent-child hierarchies:

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
            new() { Id = 0, ProductId = 1, Quantity = 5 },   // INSERT
            new() { Id = 123, ProductId = 2, Quantity = 3 }  // UPDATE
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

Graph upserts also support `NavigationFilter` to control which child collections are traversed. See [Navigation Filtering](graph-operations.md#navigation-filtering) for details.

## UpsertBatchResult Properties

```csharp
result.InsertedEntities   // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.UpdatedEntities    // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.InsertedIds        // IReadOnlyList<TKey> - Just the inserted IDs
result.UpdatedIds         // IReadOnlyList<TKey> - Just the updated IDs
result.SuccessfulIds      // IReadOnlyList<TKey> - All successful IDs (inserted + updated)
result.InsertedCount      // Count of inserts
result.UpdatedCount       // Count of updates
result.Failures           // List<UpsertBatchFailure<TKey>> with EntityIndex, AttemptedOperation
result.GraphHierarchy     // For graph upserts: parent ID -> GraphNode
result.TraversalInfo      // Graph traversal statistics
```

## Handling Race Conditions

**Preferred approach:** Use `DuplicateKeyStrategy.RetryAsUpdate` (see above).

For custom retry logic:

```csharp
const int maxRetries = 3;
UpsertBatchResult<int>? result = null;

for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        result = saver.UpsertBatch(products);
        break;  // Success
    }
    catch (DbUpdateException ex) when (attempt < maxRetries)
    {
        // Log and retry
        await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));

        // Refresh entities from database before retry
        foreach (var product in products)
        {
            context.Entry(product).State = EntityState.Detached;
        }
    }
}
```

## When to Avoid Upsert

Consider alternatives when:

- **High write concurrency**: Multiple processes frequently write the same keys
- **Atomic guarantees needed**: Business logic requires all-or-nothing semantics
- **Performance critical**: Database-level upsert (MERGE/ON CONFLICT) is faster

For these cases, use raw SQL or a library that supports database-native upsert operations.
