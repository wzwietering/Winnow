# Results Reference

All batch operations return detailed result objects with success/failure information.

## BatchResult&lt;TKey&gt; (Update/Delete)

Returned by `UpdateBatch`, `UpdateGraphBatch`, `DeleteBatch`, `DeleteGraphBatch`.

```csharp
// Success information
result.SuccessfulIds      // IReadOnlyList<TKey> - IDs that succeeded
result.SuccessCount       // Number of successful entities
result.IsCompleteSuccess  // All succeeded
result.IsPartialSuccess   // Some succeeded, some failed
result.IsCompleteFailure  // All failed

// Failure information
result.Failures           // IReadOnlyList<BatchFailure<TKey>>
result.FailureCount       // Number of failed entities
result.FailedIds          // IReadOnlyList<TKey> - IDs that failed

// Graph operations (null for non-graph)
result.GraphHierarchy     // IReadOnlyList<GraphNode<TKey>>? - entity hierarchy
result.TraversalInfo      // GraphTraversalResult - traversal statistics

// Computed properties
result.TotalProcessed     // int - SuccessCount + FailureCount
result.SuccessRate        // double (0-1) - ratio of successful to total
result.WasCancelled       // bool - true if cancelled before completing
result.TotalRetries       // int - transient failure retries (0 without RetryOptions)

// Performance metrics
result.DatabaseRoundTrips // Number of DB calls made
result.Duration           // Total time elapsed
```

### BatchFailure&lt;TKey&gt;

```csharp
failure.EntityId      // TKey - ID of the failed entity
failure.ErrorMessage  // string - Error description
failure.Reason        // FailureReason enum - Categorized failure type
failure.Exception     // Exception? - The original exception, if available
```

### FailureReason

| Value | Description |
|-------|-------------|
| `ValidationError` | Entity failed validation |
| `ConcurrencyConflict` | Optimistic concurrency conflict (row was modified) |
| `DatabaseConstraint` | Database constraint violation (FK, check, etc.) |
| `DuplicateKey` | Primary key or unique constraint violation |
| `Cancelled` | Operation was cancelled via CancellationToken |
| `UnknownError` | Unclassified error |

## InsertBatchResult&lt;TKey&gt;

Returned by `InsertBatch`, `InsertGraphBatch`.

```csharp
// Success information
result.InsertedEntities   // IReadOnlyList<InsertedEntity<TKey>> - detailed insert info
result.InsertedIds        // IReadOnlyList<TKey> - just the generated IDs
result.SuccessCount       // Number of successful inserts
result.IsCompleteSuccess  // All succeeded

// Failure information
result.Failures           // IReadOnlyList<InsertBatchFailure>
result.FailureCount       // Number of failed inserts

// Graph operations (null for non-graph)
result.GraphHierarchy     // IReadOnlyList<GraphNode<TKey>>? - entity hierarchy
result.TraversalInfo      // GraphTraversalResult - traversal statistics

// Performance metrics
result.DatabaseRoundTrips // Number of DB calls made
result.Duration           // Total time elapsed
```

### InsertedEntity&lt;TKey&gt;

```csharp
inserted.Id            // TKey - Generated ID
inserted.OriginalIndex // int - Position in original input list
inserted.Entity        // TEntity - Reference to the entity
```

### InsertBatchFailure

```csharp
failure.EntityIndex   // int - Position in original input list
failure.ErrorMessage  // string - Error description
failure.Reason        // FailureReason enum - Categorized failure type
failure.Exception     // Exception? - The original exception, if available
```

## UpsertBatchResult&lt;TKey&gt;

Returned by `UpsertBatch`, `UpsertGraphBatch`.

```csharp
// Insert results
result.InsertedEntities   // IReadOnlyList<UpsertedEntity<TKey>> - inserted entities
result.InsertedIds        // IReadOnlyList<TKey> - just inserted IDs
result.InsertedCount      // Number of inserts

// Update results
result.UpdatedEntities    // IReadOnlyList<UpsertedEntity<TKey>> - updated entities
result.UpdatedIds         // IReadOnlyList<TKey> - just updated IDs
result.UpdatedCount       // Number of updates

// Combined
result.SuccessfulIds      // IReadOnlyList<TKey> - all successful IDs
result.SuccessCount       // Total successful operations
result.IsCompleteSuccess  // All succeeded

// Failure information
result.Failures           // IReadOnlyList<UpsertBatchFailure<TKey>>
result.FailureCount       // Number of failures

// Graph operations (null for non-graph)
result.GraphHierarchy     // IReadOnlyList<GraphNode<TKey>>? - entity hierarchy
result.TraversalInfo      // GraphTraversalResult - traversal statistics

// Performance metrics
result.DatabaseRoundTrips // Number of DB calls made
result.Duration           // Total time elapsed
```

### UpsertedEntity&lt;TKey&gt;

```csharp
upserted.Id            // TKey - Entity ID
upserted.OriginalIndex // int - Position in original input list
upserted.Entity        // TEntity - Reference to the entity
upserted.Operation     // UpsertOperationType.Insert or UpsertOperationType.Update
```

### UpsertBatchFailure&lt;TKey&gt;

```csharp
failure.EntityIndex        // int - Position in original input list
failure.EntityId           // TKey? - Entity ID if known (null for default-key inserts)
failure.AttemptedOperation // UpsertOperationType - What was attempted
failure.ErrorMessage       // string - Error description
failure.Reason             // FailureReason enum - Categorized failure type
failure.Exception          // Exception? - The original exception, if available
failure.IsDefaultKey       // bool - True if entity had default key when operation was attempted
```

## GraphTraversalResult

Available on graph operation results via `result.TraversalInfo`.

```csharp
// Traversal info
traversalInfo.MaxDepthReached              // int - deepest level traversed
traversalInfo.TotalEntitiesTraversed       // int - total entities processed
traversalInfo.EntitiesByDepth              // IReadOnlyDictionary<int, int> - count per depth level

// Reference tracking
traversalInfo.ProcessedReferencesByType    // IReadOnlyDictionary<string, IReadOnlyList<TKey>>
traversalInfo.UniqueReferencesProcessed    // int - count of unique references
traversalInfo.MaxReferenceDepthReached     // int - deepest reference level

// Many-to-many tracking
traversalInfo.JoinRecordsCreated           // int - join records added
traversalInfo.JoinRecordsRemoved           // int - join records deleted
traversalInfo.JoinOperationsByNavigation   // IReadOnlyDictionary<string, (int Created, int Removed)>
```

## GraphNode&lt;TKey&gt;

Represents a node in the entity graph hierarchy.

```csharp
node.EntityId               // TKey - ID of this entity
node.EntityType             // string - CLR type name
node.Depth                  // int - depth level (0 = root)
node.Children               // IReadOnlyList<GraphNode<TKey>> - child nodes
node.GetChildIds()          // IReadOnlyList<TKey> - immediate child IDs
node.GetAllDescendantIds()  // IReadOnlyList<TKey> - all descendant IDs (recursive)
```

## Failure Isolation

Winnow separates each entity's outcome independently — the good saves are kept, and the failures are set aside:

```
Batch: [Order1 + items, Order2 + items, Order3 + items]
       ✅ saved        ❌ failed         ✅ saved

Order2's failure doesn't affect Order1 or Order3.
```

This allows you to:
1. Process successful entities normally
2. Log or retry failed entities separately
3. Report partial success to users

## Async Methods

All batch operations have async counterparts that accept a `CancellationToken`:

```csharp
// Sync
var result = saver.InsertBatch(entities);

// Async
var result = await saver.InsertBatchAsync(entities, cancellationToken);
```

Available async methods:
- `InsertBatchAsync`, `InsertGraphBatchAsync`
- `UpdateBatchAsync`, `UpdateGraphBatchAsync`
- `DeleteBatchAsync`, `DeleteGraphBatchAsync`
- `UpsertBatchAsync`, `UpsertGraphBatchAsync`

## Graph Result Properties

### GraphHierarchy

The `GraphHierarchy` property is populated only for graph operations (`InsertGraphBatch`, `UpdateGraphBatch`, etc.). It contains a list of root-level `GraphNode<TKey>` objects, each with recursive `Children`.

```csharp
// For non-graph operations, this is null
var result = saver.InsertBatch(entities);
result.GraphHierarchy  // null

// For graph operations, this contains the hierarchy
var result = saver.InsertGraphBatch(orders);
foreach (var node in result.GraphHierarchy ?? [])
{
    Console.WriteLine($"Parent {node.EntityId} has children: {string.Join(", ", node.GetChildIds())}");
}
```
