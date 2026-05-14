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

## Custom Match Expressions (MatchBy)

By default, upsert routes entities by checking whether the primary key holds its default value. For data sync scenarios where the primary key is database-generated and you want to match on a business key (e.g. `ExternalId`, `Email`, `Sku`), use `WithMatchBy` to configure the lookup:

```csharp
// Single business key:
saver.Upsert(products, new UpsertOptions()
    .WithMatchBy<Product>(p => p.Sku));

// Composite business key:
saver.Upsert(items, new UpsertOptions()
    .WithMatchBy<Item>(i => new { i.TenantId, i.ExternalId }));
```

Calling `WithMatchBy` more than once on the same options instance replaces the previously configured expression, the last call wins.

### How it works

When `MatchBy` is set, Winnow performs a single batched `SELECT` against the target table before `SaveChanges` to look up existing rows by the user-supplied columns. The lookup result drives the insert/update routing:

| Outcome | Routing |
|---------|---------|
| Match values found in the database | UPDATE (input entity's primary key and concurrency-token values are overwritten with the DB row's values) |
| No row matches | INSERT (using the caller-supplied primary key, if any) |
| Any match value is null | INSERT (null business keys can't identify an existing row) |

The lookup uses `AsNoTracking`, so it does not interfere with the change tracker or affect entities the caller has already loaded.

### Supported expression shapes

- A single property: `e => e.ExternalId`
- An anonymous projection: `e => new { e.TenantId, e.ExternalId }`

Method calls, nested member access (`e => e.Address.City`), and complex expressions are rejected **at the `WithMatchBy` call site** with a descriptive `ArgumentException`. Property mapping errors (referencing an unmapped property or a navigation property) are surfaced when the upsert runs against the configured `DbContext`.

### Match-by primary key

`MatchBy` is intended for business keys, not primary keys. Selecting the PK column (`o => o.Id` on a database-generated PK) works mechanically — the SELECT
will match by PK and copy the PK back onto itself — but adds an unnecessary round trip. Use the default behavior (omit `MatchBy`) when matching on PK.

### Race conditions

`MatchBy` reduces but does not eliminate the SELECT→SaveChanges race window. If a concurrent process inserts a row with the same business key after our SELECT but before our SaveChanges, the INSERT fails with a unique-constraint violation. Combine `MatchBy` with `DuplicateKeyStrategy.RetryAsUpdate` to
mitigate: the retry path re-queries the row that now exists, copies its primary key and concurrency-token values onto the input entity, and re-issues the save as an UPDATE.

```csharp
var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
    .WithMatchBy<CustomerOrder>(o => o.OrderNumber);
saver.Upsert(orders, options);
```

For best results, add a unique index on the columns referenced by `MatchBy`. The database will then enforce uniqueness at write time, making the retry path
correct under concurrent inserts.

### Errors

`MatchBy` throws `InvalidOperationException` (not a per-entity failure) when:

- The pre-SELECT resolves multiple rows to the same match-key tuple (ambiguous match, add a unique constraint or refine the expression).
- The input batch contains two entities with the same non-null match-key tuple.

### Limitations

- Graph upsert (`UpsertGraph`) does not support `MatchBy`. `UpsertGraphOptions` is a separate type and does not expose a `MatchBy` property.
- `MatchBy` properties must be CLR properties; shadow properties are not supported.
- Concurrency tokens copied during the merge must also be CLR properties.
- **Global query filters are rejected.** If the entity type has a `HasQueryFilter(...)` defined (typically used for soft delete or multi-tenant isolation), `MatchBy` throws `InvalidOperationException` before running any SELECT. The pre-SELECT uses `AsNoTracking()` which does *not* suppress the filter, so silently honoring it would route existing-but-filtered rows to INSERT and produce duplicates. Winnow refuses rather than corrupt data quietly. Mitigations: remove the `HasQueryFilter` configuration for the entity type, or omit `MatchBy` and use primary-key default-value routing. An opt-in to ignore filters is not present currently.

### Interaction with other UpsertOptions

`MatchBy` decides only whether each entity routes to INSERT or UPDATE. It does not affect property-level options such as those that govern which properties participate in the eventual INSERT/UPDATE statement. Combine freely.

### Performance

`MatchBy` adds one or more batched `SELECT`s before `SaveChanges`. Winnow chunks
the predicate to stay inside the database provider's per-query parameter limit:
a batch larger than the chunk size produces multiple SELECTs, and wider composite
match keys reduce the chunk size further (more parameters per row). For most
workloads with simple keys and batches up to a few hundred rows this is a single
SELECT. The retry path (`DuplicateKeyStrategy.RetryAsUpdate`) fires one additional
SELECT per refreshed entity.

### InsertedWithNullMatchKeyCount

`UpsertResult.InsertedWithNullMatchKeyCount` reports the number of entities
routed to INSERT because at least one component of their `MatchBy` projection
was null. It is:

- `null` when `WithMatchBy` was not configured on the upsert call.
- `0` when MatchBy was configured but every entity had a non-null match key.
- A positive integer when MatchBy was configured and some entities had null
  components.

A non-zero value typically indicates a data-quality issue upstream — the
business key was expected but missing. Surface this signal in your application
rather than relying on the silent insert. Distinguishing `null` from `0` lets
callers tell "feature not active" apart from "feature active, no null keys
observed."

## DuplicateKeyStrategy

Handle race conditions automatically with `DuplicateKeyStrategy`:

```csharp
var result = saver.Upsert(products, new UpsertOptions
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
var saver = new Winnower<Product, int>(context);

var products = new[]
{
    new Product { Id = 0, Name = "New Product", Price = 9.99m },     // INSERT (Id=0)
    new Product { Id = 42, Name = "Updated Name", Price = 19.99m }, // UPDATE (Id=42)
    new Product { Id = 0, Name = "Another New", Price = 5.00m }     // INSERT (Id=0)
};

var result = saver.Upsert(products);

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
var saver = new Winnower<CustomerOrder, int>(context);

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

var result = saver.UpsertGraph(orders, new UpsertGraphOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete  // Required for graph updates
});

Console.WriteLine($"Inserted: {result.InsertedCount}");
Console.WriteLine($"Updated: {result.UpdatedCount}");
```

Graph upserts also support `NavigationFilter` to control which child collections are traversed. See [Navigation Filtering](graph-operations.md#navigation-filtering) for details.

## UpsertResult Properties

```csharp
result.InsertedEntities   // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.UpdatedEntities    // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.InsertedIds        // IReadOnlyList<TKey> - Just the inserted IDs
result.UpdatedIds         // IReadOnlyList<TKey> - Just the updated IDs
result.SuccessfulIds      // IReadOnlyList<TKey> - All successful IDs (inserted + updated)
result.InsertedCount      // Count of inserts
result.UpdatedCount       // Count of updates
result.Failures           // List<UpsertFailure<TKey>> with EntityIndex, AttemptedOperation
result.GraphHierarchy     // For graph upserts: IReadOnlyList<GraphNode<TKey>>?
result.TraversalInfo      // Graph traversal statistics
```

## Handling Race Conditions

**Preferred approach:** Use `DuplicateKeyStrategy.RetryAsUpdate` (see above).

For custom retry logic:

```csharp
const int maxRetries = 3;
UpsertResult<int>? result = null;

for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        result = saver.Upsert(products);
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
