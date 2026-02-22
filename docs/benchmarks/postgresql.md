# PostgreSQL Benchmarks

Performance measurements on PostgreSQL (via Docker) using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.14.0

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 7.7 ms | 9.0 ms | 1.17x | 78.9 ms | 10.2x |
| 1,000 | 80.9 ms | 86.5 ms | 1.07x | 733.8 ms | 9.1x |
| 5,000 | 268.7 ms | 273.5 ms | 1.02x | 3,409.0 ms | 12.7x |

DivideAndConquer adds 2-17% overhead compared to raw EF Core. At 5,000 entities it's only 2% slower while providing error isolation and result tracking. Winnow adds ~31-34% memory overhead.

OneByOne is 9-13x slower than raw EF Core. This is much lower than on SQLite (66-374x) because PostgreSQL handles individual round-trips efficiently through connection pooling and server-side processing.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 11.8 ms | 75.7 ms | 242.0 ms | 429.2 ms |
| Insert O×O | 83.4 ms | 565.1 ms | 3,217.8 ms | 6,935.3 ms |
| Speedup | **7x** | **7x** | **13x** | **16x** |
| | | | | |
| **Update D&C** | 14.0 ms | 78.9 ms | 254.2 ms | 482.3 ms |
| Update O×O | 80.3 ms | 601.0 ms | 3,133.9 ms | 6,665.4 ms |
| Speedup | **6x** | **8x** | **12x** | **14x** |
| | | | | |
| **Upsert D&C** | 11.5 ms | 66.7 ms | 252.1 ms | 454.2 ms |
| Upsert O×O | 95.0 ms | 595.3 ms | 3,238.6 ms | 6,116.6 ms |
| Speedup | **8x** | **9x** | **13x** | **13x** |
| | | | | |
| **Delete D&C** | 7.6 ms | 56.6 ms | 208.9 ms | — |
| Delete O×O | 74.3 ms | 688.9 ms | 3,317.4 ms | — |
| Speedup | **10x** | **12x** | **16x** | — |

DivideAndConquer is 6-16x faster across every operation and batch size. The speedup ratios are lower than SQLite (46-659x) because PostgreSQL's OneByOne performance is already efficient — ~600 ms per 1,000 entities vs ~9,000 ms on SQLite. Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~600 ms per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~5.7x
- Both strategies are significantly faster than on SQLite in absolute terms

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.07 MB | 10.28 MB | 48.61 MB | 96.56 MB |
| Insert O×O | 1.42 MB | 12.35 MB | 61.40 MB | 123.56 MB |
| Reduction | 25% | 17% | 21% | 22% |
| | | | | |
| Update D&C | 1.10 MB | 10.96 MB | 51.20 MB | 101.33 MB |
| Delete D&C | 0.80 MB | 7.93 MB | 37.33 MB | — |

DivideAndConquer uses 17-25% less memory than OneByOne. At 10,000 entities the per-entity cost is ~10 KB (D&C) vs ~12 KB (OneByOne). PostgreSQL uses less memory per entity than SQLite because Npgsql's provider is more memory-efficient.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 107 ms | 383 ms | 1,756 ms |
| InsertGraph O×O | 343 ms | 2,823 ms | 14,351 ms |
| Speedup | **3.2x** | **7.4x** | **8.2x** |
| | | | |
| **UpsertGraph D&C** | 112 ms | 358 ms | 1,782 ms |
| UpsertGraph O×O | 285 ms | 2,234 ms | 11,257 ms |
| Speedup | **2.5x** | **6.2x** | **6.3x** |
| | | | |
| **DeleteGraph D&C** | 39 ms | 349 ms | 1,421 ms |
| DeleteGraph O×O | 219 ms | 1,663 ms | 7,574 ms |
| Speedup | **5.6x** | **4.8x** | **5.3x** |

Graph D&C speedup (2.5-8.2x) is lower than flat operations (6-16x). Navigation property traversal and entity attachment overhead compresses the advantage. DeleteGraph is the fastest graph operation at 100 and 5,000 roots.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 503 MB | ~20 KB |
| UpsertGraph D&C | 733 MB | ~29 KB |
| DeleteGraph D&C | 536 MB | ~21 KB |
| Flat Insert D&C @ 5K (for comparison) | 49 MB | ~10 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~29 KB/entity because it loads existing entities, tracks changes, and traverses navigations.

## ParallelBatchSaver

Tests `ParallelBatchSaver` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 79 ms | **218 ms** |
| 2 | 69 ms | 277 ms |
| 4 | **62 ms** | 214 ms |
| 8 | 62 ms | 240 ms |

**Parallelism shows modest benefit at small batch sizes.** At 1,000 entities, DOP 4 is 21% faster than DOP 1, and the improvement plateaus at DOP 4-8. At 5,000 entities the results are inconsistent — DOP 1 and DOP 4 are similar while DOP 2 and 8 are slower, likely due to connection pool contention and PostgreSQL's MVCC overhead on concurrent writes.

Memory is nearly identical across all DOP values (~10.4 MB @ 1K, ~49 MB @ 5K), confirming that partitioning does not affect total allocation.

For PostgreSQL with small-to-medium batches, DOP 4 may provide a modest speedup. For large batches (5K+), stick with `BatchSaver` or DOP 1 to avoid contention overhead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 593 ms | 71 ms | **8.4x** |
| 10% | 527 ms | 357 ms | **1.5x** |
| 25% | 429 ms | 394 ms | **1.1x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 8.4x faster, but at 25% the two strategies are nearly equal. Because PostgreSQL's OneByOne is already fast, even small failure rates quickly erode D&C's advantage.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (593 → 429 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (71 → 394 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 10.3 MB | 12.3 MB |
| 10% | 25.3 MB | 11.4 MB |
| 25% | 27.1 MB | 10.0 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). On PostgreSQL the crossover point is lower than on SQLite — at 25% failures the strategies are essentially equal. Pre-validate entities before calling the batch operation if you expect frequent failures.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| Small-to-medium batches with DOP 4 | `ParallelBatchSaver` with `DivideAndConquer` |
| Large batches (5K+) | `BatchSaver` with `DivideAndConquer` |

## Running the Benchmarks

```bash
# All providers (requires Docker for PostgreSQL/SQL Server)
dotnet run -c Release --project benchmarks/Winnow.Benchmarks

# Specific benchmark class
dotnet run -c Release --project benchmarks/Winnow.Benchmarks -- --filter '*InsertBenchmarks*'

# SQLite only (no Docker required)
dotnet run -c Release --project benchmarks/Winnow.Benchmarks -- --sqlite-only
```

Results are written to `benchmarks/Winnow.Benchmarks/BenchmarkDotNet.Artifacts/results/`.
