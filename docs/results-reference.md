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
result.Failures           // List<BatchFailure<TKey>>
result.FailureCount       // Number of failed entities

// Graph operations
result.ChildIdsByParentId // Dictionary<TKey, IReadOnlyList<TKey>> - child IDs by parent
result.TraversalInfo      // GraphTraversalResult - traversal statistics

// Performance metrics
result.DatabaseRoundTrips // Number of DB calls made
result.Duration           // Total time elapsed
```

### BatchFailure&lt;TKey&gt;

```csharp
failure.EntityId      // TKey - ID of the failed entity
failure.ErrorMessage  // string - Error description
failure.Reason        // FailureReason enum - Categorized failure type
```

## InsertBatchResult&lt;TKey&gt;

Returned by `InsertBatch`, `InsertGraphBatch`.

```csharp
// Success information
result.InsertedEntities   // List<InsertedEntity<TKey>> - detailed insert info
result.InsertedIds        // IReadOnlyList<TKey> - just the generated IDs
result.SuccessCount       // Number of successful inserts
result.IsCompleteSuccess  // All succeeded

// Failure information
result.Failures           // List<InsertBatchFailure>
result.FailureCount       // Number of failed inserts

// Graph operations
result.ChildIdsByParentId // Dictionary<TKey, IReadOnlyList<TKey>> - child IDs by parent
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
```

## UpsertBatchResult&lt;TKey&gt;

Returned by `UpsertBatch`, `UpsertGraphBatch`.

```csharp
// Insert results
result.InsertedEntities   // List<UpsertedEntity<TKey>> - inserted entities
result.InsertedIds        // IReadOnlyList<TKey> - just inserted IDs
result.InsertedCount      // Number of inserts

// Update results
result.UpdatedEntities    // List<UpsertedEntity<TKey>> - updated entities
result.UpdatedIds         // IReadOnlyList<TKey> - just updated IDs
result.UpdatedCount       // Number of updates

// Combined
result.SuccessfulIds      // IReadOnlyList<TKey> - all successful IDs
result.SuccessCount       // Total successful operations
result.IsCompleteSuccess  // All succeeded

// Failure information
result.Failures           // List<UpsertBatchFailure<TKey>>
result.FailureCount       // Number of failures

// Graph operations
result.GraphHierarchy     // Dictionary<TKey, GraphNode<TKey>> - full hierarchy
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
upserted.Operation     // UpsertOperation.Insert or UpsertOperation.Update
```

### UpsertBatchFailure&lt;TKey&gt;

```csharp
failure.EntityIndex       // int - Position in original input list
failure.AttemptedOperation // UpsertOperation - What was attempted
failure.ErrorMessage      // string - Error description
```

## GraphTraversalResult

Available on graph operation results via `result.TraversalInfo`.

```csharp
// Reference tracking
traversalInfo.ProcessedReferencesByType    // Dictionary<string, List<object>> - refs by type
traversalInfo.UniqueReferencesProcessed    // int - count of unique references

// Many-to-many tracking
traversalInfo.JoinRecordsCreated           // int - join records added
traversalInfo.JoinRecordsRemoved           // int - join records deleted
traversalInfo.JoinOperationsByNavigation   // Dictionary<string, JoinStats> - per-navigation

// Traversal info
traversalInfo.MaxDepthReached              // int - deepest level traversed
traversalInfo.EntitiesVisited              // int - total entities processed
```

## GraphNode&lt;TKey&gt;

For upsert graph results, represents a node in the entity hierarchy.

```csharp
node.EntityId    // TKey - ID of this entity
node.Operation   // UpsertOperation - Insert or Update
node.Children    // List<GraphNode<TKey>> - child nodes
```

## Failure Isolation

Each entity (or graph) succeeds or fails independently:

```
Batch: [Order1 + items, Order2 + items, Order3 + items]
       ✅ saved        ❌ failed         ✅ saved

Order2's failure doesn't affect Order1 or Order3.
```

This allows you to:
1. Process successful entities normally
2. Log or retry failed entities separately
3. Report partial success to users
