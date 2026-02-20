# EfCoreUtils

Batch operations for Entity Framework Core with per-entity failure isolation.

```csharp
var saver = new BatchSaver<Product, int>(context);
var result = saver.InsertBatch(products);

if (!result.IsCompleteSuccess)
{
    foreach (var failure in result.Failures)
        _logger.LogWarning("Insert failed: {Message}", failure.ErrorMessage);
}
```

## Motivation

Entity Framework Core's `SaveChanges()` operates atomically: a single invalid entity causes the entire batch to fail. EfCoreUtils provides failure isolation, allowing valid entities to persist while capturing detailed failure information for invalid ones.

## Installation

```bash
dotnet add package EfCoreUtils
```

**Requirements:** .NET 10.0+, Entity Framework Core 10.0+

## Table of Contents

- [Quick Start](#quick-start)
- [Choosing Your Operation](#choosing-your-operation)
- [Basic Operations](#basic-operations)
- [Strategies](#strategies)
- [Handling Results](#handling-results)
- [When to Use What](#when-to-use-what)
- [When NOT to Use This](#when-not-to-use-this)
- [Parallel Batch Processing](#parallel-batch-processing)
- [Advanced Topics](#advanced-topics)
- [Common Mistakes](#common-mistakes)

## Quick Start

```csharp
// Create a saver for your entity type and key type
var saver = new BatchSaver<Product, int>(context);

// Insert new entities
var insertResult = saver.InsertBatch(newProducts);
Console.WriteLine($"Inserted: {insertResult.SuccessCount}, Failed: {insertResult.FailureCount}");

// Update existing entities
var updateResult = saver.UpdateBatch(existingProducts);

// Delete entities
var deleteResult = saver.DeleteBatch(productsToRemove);

// Always check for failures
foreach (var failure in updateResult.Failures)
{
    Console.WriteLine($"Entity {failure.EntityId} failed: {failure.ErrorMessage}");
}

// All operations have async versions
var asyncResult = await saver.InsertBatchAsync(newProducts, cancellationToken);
```

### Supported Key Types

BatchSaver supports any key type that implements `IEquatable<TKey>`:

```csharp
var saver = new BatchSaver<Product, int>(context);      // Integer keys
var saver = new BatchSaver<Order, long>(context);       // Long keys
var saver = new BatchSaver<Document, Guid>(context);    // GUID keys
var saver = new BatchSaver<Setting, string>(context);   // String keys
```

For composite keys, see [Composite Keys](docs/composite-keys.md).

## Choosing Your Operation

```
What do you need to do?
├── Insert new entities only ──────────────────→ InsertBatch / InsertGraphBatch
├── Update existing entities only ─────────────→ UpdateBatch / UpdateGraphBatch
├── Delete entities ───────────────────────────→ DeleteBatch / DeleteGraphBatch
└── Insert OR update (based on key) ───────────→ UpsertBatch / UpsertGraphBatch

Do your entities have children (navigation properties)?
├── No  → Use the non-graph method (InsertBatch, UpdateBatch, etc.)
└── Yes → Use the graph method (InsertGraphBatch, UpdateGraphBatch, etc.)
```

## Basic Operations

### Insert

```csharp
var result = saver.InsertBatch(products, new InsertBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Access generated IDs
foreach (var inserted in result.InsertedEntities)
{
    Console.WriteLine($"Index {inserted.OriginalIndex} got ID {inserted.Id}");
}
```

### Update

```csharp
var result = saver.UpdateBatch(products);

Console.WriteLine($"Updated: {result.SuccessCount}");
```

### Delete

```csharp
var result = saver.DeleteBatch(productsToRemove);
```

### Upsert

Performs INSERT or UPDATE based on key value (default key → INSERT, non-default → UPDATE).

**Warning: Not a database MERGE.** See [Upsert Operations](docs/upsert-operations.md) for race condition details.

```csharp
var products = new[]
{
    new Product { Id = 0, Name = "New" },   // INSERT (Id=0)
    new Product { Id = 42, Name = "Updated" } // UPDATE
};

var result = saver.UpsertBatch(products, new UpsertBatchOptions
{
    DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate  // Handle race conditions
});
Console.WriteLine($"Inserted: {result.InsertedCount}, Updated: {result.UpdatedCount}");
```

### Graph Operations

Handle parent entities with their children:

```csharp
var orders = new List<CustomerOrder>
{
    new CustomerOrder
    {
        CustomerName = "Alice",
        OrderItems = new List<OrderItem>
        {
            new OrderItem { ProductId = 1, Quantity = 2 }
        }
    }
};

var result = saver.InsertGraphBatch(orders);

// For updates, specify orphan behavior
var updateResult = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});
```

See [Graph Operations](docs/graph-operations.md) for full documentation.

## Strategies

| Strategy | Best For | Round Trips |
|----------|----------|-------------|
| **OneByOne** | High failure rates, predictability | N |
| **DivideAndConquer** | Low failure rates (<5%) | 1 at 0%, ~150 at 1% |

### Performance (1000 entities, SQLite)

| Failure Rate | OneByOne | DivideAndConquer | Recommendation |
|--------------|----------|------------------|----------------|
| 0%           | 1000     | 1                | DivideAndConquer |
| 1%           | 1000     | 147              | DivideAndConquer |
| 5%           | 1000     | 523              | OneByOne |
| 25%+         | 1000     | 1499+            | OneByOne |

With network databases (SQL Server, PostgreSQL), DivideAndConquer wins more clearly due to latency savings.

### Benchmarked Timings (SQLite)

DivideAndConquer adds near-zero overhead compared to raw `SaveChanges()`:

| Entities | Raw EF Core | DivideAndConquer | OneByOne |
|----------|-------------|------------------|----------|
| 1,000 | 54 ms | 68-71 ms | 2,500+ ms |
| 5,000 | 117 ms | 107-135 ms | 12,000+ ms |
| 10,000 | — | 163-179 ms | 25,000+ ms |

Graph operations (parent + children) are 2-3x more expensive per entity in memory. Pre-validate your data when using DivideAndConquer — at 25% failure rate, it degrades to near-OneByOne performance.

For full results including graph, parallel, and failure rate benchmarks, see [SQLite Benchmarks](docs/benchmarks/sqlite.md).

## Handling Results

All operations return detailed results:

```csharp
var result = saver.UpdateBatch(products);

// Check overall status
if (result.IsCompleteSuccess) { /* all succeeded */ }
if (result.IsPartialSuccess) { /* some succeeded, some failed */ }
if (result.IsCompleteFailure) { /* all failed */ }

// Access successes
foreach (var id in result.SuccessfulIds)
{
    Console.WriteLine($"Saved: {id}");
}

// Access failures with details
foreach (var failure in result.Failures)
{
    Console.WriteLine($"Failed {failure.EntityId}: {failure.ErrorMessage}");
}

// Performance metrics
Console.WriteLine($"Round trips: {result.DatabaseRoundTrips}");
Console.WriteLine($"Duration: {result.Duration}");
```

For full result type documentation, see [Results Reference](docs/results-reference.md).

## When to Use What

| Scenario | Method | Strategy |
|----------|--------|----------|
| Insert new entities | `InsertBatch` | DivideAndConquer (if <5% failures) |
| Insert parent + children | `InsertGraphBatch` | DivideAndConquer |
| Update existing entities | `UpdateBatch` | DivideAndConquer (if <5% failures) |
| Update parent + children | `UpdateGraphBatch` | Set OrphanBehavior explicitly |
| Delete entities | `DeleteBatch` | DivideAndConquer (if <5% failures) |
| Delete parent + children | `DeleteGraphBatch` | Set CascadeBehavior explicitly |
| Include many-to-one refs | `*GraphBatch` | Set `IncludeReferences = true` |
| Include many-to-many | `*GraphBatch` | Set `IncludeManyToMany = true` |
| High failure rate (>25%) | Any | OneByOne |

## When NOT to Use This

EfCoreUtils is not the right choice for every scenario:

- **Single entity operations**: Standard EF Core `Add`/`Update`/`Remove` + `SaveChanges()` is simpler
- **All-or-nothing transactions**: If a single failure should roll back everything, use standard `SaveChanges()`
- **True database MERGE**: For high-concurrency upserts, use raw SQL with `MERGE` (SQL Server) or `ON CONFLICT` (PostgreSQL)
- **Bulk operations without failure tracking**: Libraries like EFCore.BulkExtensions offer higher throughput when you do not need per-entity failure isolation
- **Read-heavy workloads**: This library focuses on write operations

## Parallel Batch Processing

For large batches where database I/O is the bottleneck, `ParallelBatchSaver` distributes work across multiple `DbContext` instances:

```csharp
// Requires a factory that creates a new DbContext on each call
var saver = new ParallelBatchSaver<Product, int>(
    () => new AppDbContext(options),
    maxDegreeOfParallelism: 4);

var result = await saver.InsertBatchAsync(products, cancellationToken);
```

Or use `IDbContextFactory<TContext>`:

```csharp
var saver = factory.CreateParallelBatchSaver<Product, int, AppDbContext>(maxDegreeOfParallelism: 4);
```

**Key differences from BatchSaver:**

| | BatchSaver | ParallelBatchSaver |
|---|---|---|
| **Context** | Single shared context | New context per partition |
| **Atomicity** | All-or-nothing per batch | Per-partition (non-atomic) |
| **Async** | Sequential I/O | Parallel I/O |
| **Sync methods** | Normal execution | Falls back to single context |

**When to use ParallelBatchSaver:**
- Large batches (100+ entities) with high-latency database connections
- Sufficient connection pool capacity for concurrent operations
- Failure isolation per partition is acceptable

**When NOT to use ParallelBatchSaver:**
- Operations requiring atomicity (all-or-nothing)
- Operations requiring same-context change tracking (orphan detection)
- Small batches where parallelism overhead outweighs benefits

**Non-atomicity warning:** Each partition commits independently. If one partition fails, others that already committed will NOT be rolled back.

## Advanced Topics

Detailed documentation for complex scenarios:

- [Composite Keys](docs/composite-keys.md) - Multi-column primary keys
- [Graph Operations](docs/graph-operations.md) - Parent-child hierarchies
- [Many-to-Many Relationships](docs/many-to-many.md) - Skip navigations and join entities
- [Reference Navigation](docs/reference-navigation.md) - Many-to-one relationships
- [Self-Referencing Hierarchies](docs/self-referencing.md) - Recursive entity structures
- [Upsert Operations](docs/upsert-operations.md) - Insert-or-update with race condition handling
- [Results Reference](docs/results-reference.md) - Full result type API
- [Options Reference](docs/api/options-reference.md) - All configuration options
- [SQLite Benchmarks](docs/benchmarks/sqlite.md) - Performance data and strategy guidance

## Common Mistakes

### 1. Ignoring Failures

```csharp
// Wrong: Assuming success
saver.InsertBatch(products);

// Correct: Always check results
var result = saver.InsertBatch(products);
if (!result.IsCompleteSuccess)
{
    foreach (var f in result.Failures)
        _logger.LogError("Failed to insert: {Error}", f.ErrorMessage);
}
```

### 2. Using Graph Methods for Simple Entities

```csharp
// Unnecessary overhead
saver.InsertGraphBatch(simpleProducts);

// Better: Use non-graph method
saver.InsertBatch(simpleProducts);
```

### 3. Forgetting OrphanBehavior on Updates

```csharp
// Throws exception if children were removed
saver.UpdateGraphBatch(orders);

// Explicit about what happens to removed children
saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});
```

### 4. Assuming Upsert Is Atomic

```csharp
// Race condition possible between key check and save
saver.UpsertBatch(products);

// Better: Add retry logic for high-concurrency scenarios
// See docs/upsert-operations.md for details
```

### 5. Using DivideAndConquer with High Failure Rates

```csharp
// Slower than OneByOne when many failures expected
saver.InsertBatch(untrustedData, new InsertBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Better: Use OneByOne for untrusted/validation-heavy data
saver.InsertBatch(untrustedData, new InsertBatchOptions
{
    Strategy = BatchStrategy.OneByOne
});
```
