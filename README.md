# Winnow

*Separate the good saves from the bad.*

Batch operations for Entity Framework Core with per-entity failure isolation.

```csharp
var saver = new Winnower<Product, int>(context);
var result = saver.Insert(products);

if (!result.IsCompleteSuccess)
{
    foreach (var failure in result.Failures)
        _logger.LogWarning("Insert failed: {Message}", failure.ErrorMessage);
}
```

## Motivation

Entity Framework Core's `SaveChanges()` operates atomically: a single invalid entity causes the entire batch to fail. Winnow — named for the process of separating grain from chaff — winnows out the failures, allowing valid entities to persist while capturing detailed failure information for invalid ones.

## Installation

```bash
dotnet add package Winnow
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
var saver = new Winnower<Product, int>(context);

// Insert new entities
var insertResult = saver.Insert(newProducts);
Console.WriteLine($"Inserted: {insertResult.SuccessCount}, Failed: {insertResult.FailureCount}");

// Update existing entities
var updateResult = saver.Update(existingProducts);

// Delete entities
var deleteResult = saver.Delete(productsToRemove);

// Always check for failures
foreach (var failure in updateResult.Failures)
{
    Console.WriteLine($"Entity {failure.EntityId} failed: {failure.ErrorMessage}");
}

// All operations have async versions
var asyncResult = await saver.InsertAsync(newProducts, cancellationToken);
```

### Supported Key Types

Winnower supports any key type that implements `IEquatable<TKey>`:

```csharp
var saver = new Winnower<Product, int>(context);      // Integer keys
var saver = new Winnower<Order, long>(context);       // Long keys
var saver = new Winnower<Document, Guid>(context);    // GUID keys
var saver = new Winnower<Setting, string>(context);   // String keys
```

For composite keys, see [Composite Keys](docs/composite-keys.md).

### Dependency Injection

Register Winnow with your DI container:

```csharp
services.AddWinnow<AppDbContext>();
```

Then inject `IWinnower<TEntity, TKey>` or `IWinnower<TEntity>`:

```csharp
public class ProductService(IWinnower<Product, int> saver)
{
    public WinnowResult<int> ImportProducts(List<Product> products)
        => saver.Update(products);
}
```

## Choosing Your Operation

```
What do you need to do?
├── Insert new entities only ──────────────────→ Insert / InsertGraph
├── Update existing entities only ─────────────→ Update / UpdateGraph
├── Delete entities ───────────────────────────→ Delete / DeleteGraph
└── Insert OR update (based on key) ───────────→ Upsert / UpsertGraph

Do your entities have children (navigation properties)?
├── No  → Use the non-graph method (Insert, Update, etc.)
└── Yes → Use the graph method (InsertGraph, UpdateGraph, etc.)
```

## Basic Operations

### Insert

```csharp
var result = saver.Insert(products, new InsertOptions
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
var result = saver.Update(products);

Console.WriteLine($"Updated: {result.SuccessCount}");
```

### Delete

```csharp
var result = saver.Delete(productsToRemove);
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

var result = saver.Upsert(products, new UpsertOptions
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

var result = saver.InsertGraph(orders);

// For updates, specify orphan behavior
var updateResult = saver.UpdateGraph(orders, new GraphOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});

// Filter which navigations are traversed
var filter = NavigationFilter.Include()
    .Navigation<CustomerOrder>(o => o.OrderItems);

var filteredResult = saver.InsertGraph(orders, new InsertGraphOptions
{
    NavigationFilter = filter  // Only traverses OrderItems, skips deeper levels
});
```

See [Graph Operations](docs/graph-operations.md) for full documentation.

## Strategies

| Strategy | How it works | Best for |
|----------|--------------|----------|
| **DivideAndConquer** | Saves entire batch at once; on failure, binary-splits to isolate bad entities | Low failure rates (<5%) |
| **OneByOne** | Saves each entity individually | High failure rates, predictable cost |

### Benchmarked Performance

DivideAndConquer adds minimal overhead vs raw `SaveChanges()` (2-9%) while providing error isolation. On SQL Server at 5K+ entities, it's actually **faster** than raw EF Core.

**Flat insert, DivideAndConquer (milliseconds):**

| Entities | SQLite | PostgreSQL | SQL Server |
|----------|--------|------------|------------|
| 100 | 17 ms | 12 ms | 20 ms |
| 1,000 | 78 ms | 76 ms | 95 ms |
| 5,000 | 119 ms | 242 ms | 253 ms |
| 10,000 | 197 ms | 429 ms | 401 ms |

OneByOne is **6-659x slower** depending on provider and batch size. The gap widens at scale because DivideAndConquer scales sub-linearly while OneByOne scales linearly.

### Failure Rates Change Everything

DivideAndConquer's advantage erodes sharply when entities fail validation:

| Failure Rate | SQLite D&C | SQL Server D&C | PostgreSQL D&C |
|--------------|------------|----------------|----------------|
| 0% | 76 ms (125x faster) | 85 ms (75x) | 71 ms (8x) |
| 10% | 2,411 ms (3.4x) | 1,869 ms (3.0x) | 357 ms (1.5x) |
| 25% | 3,388 ms (1.9x) | 2,726 ms (1.8x) | 394 ms (1.1x) |

At 25% failures, the strategies perform nearly the same. **Pre-validate your entities** if you expect failures above ~5% — this preserves DivideAndConquer's speed advantage.

### Graph and Memory

Graph operations (parent + children) use 2-3x more memory per entity (~20-31 KB vs ~9-11 KB for flat). UpsertGraph is the most expensive due to loading existing entities and tracking changes. DivideAndConquer speedups are compressed but still significant (3-77x depending on provider).

For full results, see [SQLite](docs/benchmarks/sqlite.md), [PostgreSQL](docs/benchmarks/postgresql.md), and [SQL Server](docs/benchmarks/sqlserver.md) benchmarks.

## Handling Results

Every batch operation winnows out the failures, giving you detailed results for each entity:

```csharp
var result = saver.Update(products);

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

| Scenario | Method | Notes |
|----------|--------|-------|
| Insert new entities | `Insert` | DivideAndConquer unless >5% failures |
| Update existing entities | `Update` | DivideAndConquer unless >5% failures |
| Delete entities | `Delete` | DivideAndConquer unless >5% failures |
| Insert parent + children | `InsertGraph` | DivideAndConquer; 2-3x more memory |
| Update parent + children | `UpdateGraph` | Set `OrphanBehavior` explicitly |
| Delete parent + children | `DeleteGraph` | Set `CascadeBehavior` explicitly |
| Many-to-one references | `*GraphBatch` | Set `IncludeReferences = true` |
| Many-to-many relationships | `*GraphBatch` | Set `IncludeManyToMany = true` |
| High failure rate (>5%) | Any | Pre-validate, then DivideAndConquer |
| Per-entity error isolation needed | Any | OneByOne |

## When NOT to Use This

Winnow is not the right choice for every scenario:

- **Single entity operations**: Standard EF Core `Add`/`Update`/`Remove` + `SaveChanges()` is simpler
- **All-or-nothing transactions**: If a single failure should roll back everything, use standard `SaveChanges()`
- **True database MERGE**: For high-concurrency upserts, use raw SQL with `MERGE` (SQL Server) or `ON CONFLICT` (PostgreSQL)
- **Bulk operations without failure tracking**: Libraries like EFCore.BulkExtensions offer higher throughput when you do not need per-entity failure isolation
- **Read-heavy workloads**: This library focuses on write operations

## Parallel Batch Processing

`ParallelWinnower` distributes work across multiple `DbContext` instances:

```csharp
// Requires a factory that creates a new DbContext on each call
var saver = new ParallelWinnower<Product, int>(
    () => new AppDbContext(options),
    maxDegreeOfParallelism: 4);

var result = await saver.InsertAsync(products, cancellationToken);
```

Or use `IDbContextFactory<TContext>`:

```csharp
var saver = factory.CreateParallelWinnower<Product, int, AppDbContext>(maxDegreeOfParallelism: 4);
```

**Benchmark reality check:** In our benchmarks, ParallelWinnower showed **no consistent benefit** across any provider. SQLite uses file-level locking so parallel writes contend. PostgreSQL and SQL Server showed marginal improvements (10-23%) at DOP 4 for small batches, but the gains disappeared or reversed at larger sizes due to connection pool contention. **For most workloads, standard `Winnower` with DivideAndConquer is faster and simpler.** See the [benchmark docs](docs/benchmarks/postgresql.md) for details.

| | Winnower | ParallelWinnower |
|---|---|---|
| **Context** | Single shared context | New context per partition |
| **Atomicity** | All-or-nothing per batch | Per-partition (non-atomic) |
| **Async** | Sequential I/O | Parallel I/O |
| **Sync methods** | Normal execution | Falls back to single context |

ParallelWinnower may still help with high-latency remote databases (cloud SQL with cross-region latency) where the round-trip cost dominates — a scenario our local Docker benchmarks don't capture. If you use it, note that each partition commits independently: if one fails, others that already committed will NOT be rolled back.

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
- [PostgreSQL Benchmarks](docs/benchmarks/postgresql.md) - Network database performance
- [SQL Server Benchmarks](docs/benchmarks/sqlserver.md) - Network database performance

## Common Mistakes

### 1. Ignoring Failures

```csharp
// Wrong: Assuming success
saver.Insert(products);

// Correct: Always check results
var result = saver.Insert(products);
if (!result.IsCompleteSuccess)
{
    foreach (var f in result.Failures)
        _logger.LogError("Failed to insert: {Error}", f.ErrorMessage);
}
```

### 2. Using Graph Methods for Simple Entities

```csharp
// Unnecessary overhead
saver.InsertGraph(simpleProducts);

// Better: Use non-graph method
saver.Insert(simpleProducts);
```

### 3. Forgetting OrphanBehavior on Updates

```csharp
// Throws exception if children were removed
saver.UpdateGraph(orders);

// Explicit about what happens to removed children
saver.UpdateGraph(orders, new GraphOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});
```

### 4. Assuming Upsert Is Atomic

```csharp
// Race condition possible between key check and save
saver.Upsert(products);

// Better: Add retry logic for high-concurrency scenarios
// See docs/upsert-operations.md for details
```

### 5. Using DivideAndConquer with High Failure Rates

```csharp
// Slower than OneByOne when many failures expected
saver.Insert(untrustedData, new InsertOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Better: Use OneByOne for untrusted/validation-heavy data
saver.Insert(untrustedData, new InsertOptions
{
    Strategy = BatchStrategy.OneByOne
});
```
