# PostgreSQL Benchmarks

Performance measurements on PostgreSQL (via Docker) using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.15.8

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 10.2 ms | 8.1 ms | **0.79x** | 78.2 ms | 7.7x |
| 1,000 | 79.9 ms | 73.7 ms | **0.92x** | 574.2 ms | 7.2x |
| 5,000 | 241.5 ms | 239.3 ms | 0.99x | 2,922.5 ms | 12.1x |

DivideAndConquer performance is within measurement noise of raw EF Core at all tested batch sizes — the error bars overlap in every case. Winnow adds ~31-34% memory overhead for result tracking and metadata.

OneByOne is 7-12x slower than raw EF Core. This is much lower than on SQLite (48-322x) because PostgreSQL handles individual round-trips efficiently through connection pooling and server-side processing.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 9.7 ms | 59.3 ms | 231.3 ms | 421.1 ms |
| Insert O×O | 77.8 ms | 576.9 ms | 2,807.0 ms | 5,596.7 ms |
| Speedup | **8x** | **10x** | **12x** | **13x** |
| | | | | |
| **Update D&C** | 11.3 ms | 82.7 ms | 233.6 ms | 627.9 ms |
| Update O×O | 80.6 ms | 563.2 ms | 2,907.6 ms | 6,251.1 ms |
| Speedup | **7x** | **7x** | **12x** | **10x** |
| | | | | |
| **Upsert D&C** | 13.9 ms | 116.2 ms | 225.7 ms | 433.4 ms |
| Upsert O×O | 86.8 ms | 598.9 ms | 2,821.0 ms | 5,730.5 ms |
| Speedup | **6x** | **5x** | **13x** | **13x** |
| | | | | |
| **Delete D&C** | 7.8 ms | 62.9 ms | 179.2 ms | — |
| Delete O×O | 91.4 ms | 562.1 ms | 2,851.8 ms | — |
| Speedup | **12x** | **9x** | **16x** | — |

DivideAndConquer is 5-16x faster across every operation and batch size. The speedup ratios are lower than SQLite (19-530x) because PostgreSQL's OneByOne performance is already efficient — ~575 ms per 1,000 entities vs ~9,000 ms on SQLite. Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~575 ms per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~7x
- Both strategies are significantly faster than on SQLite in absolute terms

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.07 MB | 10.28 MB | 48.62 MB | 96.56 MB |
| Insert O×O | 1.38 MB | 12.37 MB | 61.70 MB | 123.41 MB |
| Reduction | 22% | 17% | 21% | 22% |
| | | | | |
| Update D&C | 1.10 MB | 10.96 MB | 50.82 MB | 101.71 MB |
| Delete D&C | 0.80 MB | 7.83 MB | 37.33 MB | — |

DivideAndConquer uses 17-22% less memory than OneByOne. At 10,000 entities the per-entity cost is ~10 KB (D&C) vs ~12 KB (OneByOne). PostgreSQL uses less memory per entity than SQLite because Npgsql's provider is more memory-efficient.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 79 ms | 375 ms | 1,747 ms |
| InsertGraph O×O | 314 ms | 2,684 ms | 13,283 ms |
| Speedup | **4.0x** | **7.2x** | **7.6x** |
| | | | |
| **UpsertGraph D&C** | 62 ms | 363 ms | 1,661 ms |
| UpsertGraph O×O | 276 ms | 2,127 ms | 10,480 ms |
| Speedup | **4.5x** | **5.9x** | **6.3x** |
| | | | |
| **DeleteGraph D&C** | 88 ms | 274 ms | 1,373 ms |
| DeleteGraph O×O | 185 ms | 1,405 ms | 7,239 ms |
| Speedup | **2.1x** | **5.1x** | **5.3x** |

Graph D&C speedup (2.1-7.6x) is lower than flat operations (5-16x). Navigation property traversal and entity attachment overhead compresses the advantage. DeleteGraph is the fastest graph operation at 5,000 roots.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 503 MB | ~20 KB |
| UpsertGraph D&C | 733 MB | ~29 KB |
| DeleteGraph D&C | 537 MB | ~21 KB |
| Flat Insert D&C @ 5K (for comparison) | 49 MB | ~10 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~29 KB/entity because it loads existing entities, tracks changes, and traverses navigations.

## ParallelWinnower

Tests `ParallelWinnower` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 78 ms | **218 ms** |
| 2 | 71 ms | 252 ms |
| 4 | **58 ms** | 228 ms |
| 8 | 70 ms | 237 ms |

**Parallelism shows modest benefit at small batch sizes.** At 1,000 entities, DOP 4 is 26% faster than DOP 1, and the improvement plateaus at DOP 4-8. At 5,000 entities the results are inconsistent — DOP 1 is fastest while higher DOP values are slower, likely due to connection pool contention and PostgreSQL's MVCC overhead on concurrent writes.

Memory is nearly identical across all DOP values (~10.4 MB @ 1K, ~49 MB @ 5K), confirming that partitioning does not affect total allocation.

For PostgreSQL with small-to-medium batches, DOP 4 may provide a modest speedup. For large batches (5K+), stick with `Winnower` or DOP 1 to avoid contention overhead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 552 ms | 76 ms | **7.3x** |
| 10% | 491 ms | 333 ms | **1.5x** |
| 25% | 419 ms | 381 ms | **1.1x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 7.3x faster, but at 25% the two strategies are nearly equal. Because PostgreSQL's OneByOne is already fast, even small failure rates quickly erode D&C's advantage.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (552 → 419 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (76 → 381 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 10.3 MB | 12.5 MB |
| 10% | 25.4 MB | 11.6 MB |
| 25% | 27.3 MB | 10.3 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). On PostgreSQL the crossover point is lower than on SQLite — at 25% failures the strategies are essentially equal. Pre-validate entities before calling the batch operation if you expect frequent failures.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| Small-to-medium batches with DOP 4 | `ParallelWinnower` with `DivideAndConquer` |
| Large batches (5K+) | `Winnower` with `DivideAndConquer` |

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
