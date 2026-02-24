# SQL Server Benchmarks

Performance measurements on SQL Server (via Docker) using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.15.8

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 15.2 ms | 16.1 ms | 1.06x | 640.2 ms | 42.1x |
| 1,000 | 87.7 ms | 99.0 ms | 1.13x | 5,903.8 ms | 67.3x |
| 5,000 | 212.3 ms | 217.9 ms | 1.03x | 30,651.6 ms | 144.4x |

DivideAndConquer performance is within measurement noise of raw EF Core at most batch sizes — the error bars overlap in nearly all cases. Winnow adds ~30-36% memory overhead for result tracking and metadata.

OneByOne is 42-145x slower than raw EF Core, confirming it should only be used when individual entity error isolation is required.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 17.6 ms | 110.5 ms | 234.4 ms | 388.3 ms |
| Insert O×O | 573.5 ms | 6,118.6 ms | 30,939.6 ms | 62,326.7 ms |
| Speedup | **33x** | **55x** | **132x** | **161x** |
| | | | | |
| **Update D&C** | 16.8 ms | 89.9 ms | 241.9 ms | 483.4 ms |
| Update O×O | 596.1 ms | 6,559.3 ms | 30,763.9 ms | 62,526.5 ms |
| Speedup | **35x** | **73x** | **127x** | **129x** |
| | | | | |
| **Upsert D&C** | 19.5 ms | 86.9 ms | 255.3 ms | 446.2 ms |
| Upsert O×O | 656.0 ms | 5,989.2 ms | 32,206.5 ms | 63,166.1 ms |
| Speedup | **34x** | **69x** | **126x** | **142x** |
| | | | | |
| **Delete D&C** | 15.8 ms | 59.5 ms | 174.4 ms | — |
| Delete O×O | 603.4 ms | 6,333.9 ms | 30,409.9 ms | — |
| Speedup | **38x** | **106x** | **174x** | — |

DivideAndConquer is 33-174x faster across every operation and batch size. The speedup increases with batch size, similar to SQLite but not as extreme. SQL Server's per-entity round-trip cost (~6.2 seconds per 1,000 entities for OneByOne) falls between PostgreSQL (~575 ms) and SQLite (~9,000 ms). Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~6.2 seconds per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~3.5x
- SQL Server D&C absolute times are similar to PostgreSQL, both significantly faster than SQLite at larger sizes

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.10 MB | 9.77 MB | 46.64 MB | 93.35 MB |
| Insert O×O | 1.61 MB | 15.02 MB | 74.41 MB | 149.13 MB |
| Reduction | 32% | 35% | 37% | 37% |
| | | | | |
| Update D&C | 1.10 MB | 10.90 MB | 52.99 MB | 106.07 MB |
| Delete D&C | 0.83 MB | 8.05 MB | 39.49 MB | — |

DivideAndConquer uses 32-37% less memory than OneByOne. At 10,000 entities the per-entity cost is ~9 KB (D&C) vs ~15 KB (OneByOne). SQL Server has the lowest per-entity memory cost of all three providers.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 73 ms | 313 ms | 1,489 ms |
| InsertGraph O×O | 860 ms | 9,549 ms | 46,226 ms |
| Speedup | **12x** | **31x** | **31x** |
| | | | |
| **UpsertGraph D&C** | 102 ms | 341 ms | 1,694 ms |
| UpsertGraph O×O | 782 ms | 8,607 ms | 43,916 ms |
| Speedup | **7.7x** | **25x** | **26x** |
| | | | |
| **DeleteGraph D&C** | 86 ms | 417 ms | 3,461 ms |
| DeleteGraph O×O | 759 ms | 7,603 ms | 39,218 ms |
| Speedup | **8.8x** | **18x** | **11x** |

Graph D&C speedup (7.7-31x) is higher than PostgreSQL (2.1-7.6x) but lower than SQLite (9-76x). DeleteGraph D&C at 5,000 roots is notably slower (3,461 ms) compared to InsertGraph (1,489 ms), suggesting SQL Server cascade operations add overhead at scale.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 489 MB | ~20 KB |
| UpsertGraph D&C | 728 MB | ~29 KB |
| DeleteGraph D&C | 546 MB | ~22 KB |
| Flat Insert D&C @ 5K (for comparison) | 47 MB | ~9 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~29 KB/entity because it loads existing entities, tracks changes, and traverses navigations.

## ParallelWinnower

Tests `ParallelWinnower` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 97 ms | **226 ms** |
| 2 | 82 ms | 265 ms |
| 4 | **62 ms** | 289 ms |
| 8 | 63 ms | 277 ms |

**Parallelism shows modest benefit at small batch sizes.** At 1,000 entities, DOP 4 is 36% faster than DOP 1, and the improvement plateaus at DOP 4-8. At 5,000 entities, DOP 1 is fastest while higher DOP values are slower, likely due to SQL Server's lock management and connection pool overhead.

Memory is nearly identical across all DOP values (~10 MB @ 1K, ~47 MB @ 5K), confirming that partitioning does not affect total allocation.

For SQL Server, `ParallelWinnower` with DOP 4 may provide benefit at small batch sizes, but for larger batches (5K+) stick with standard `Winnower` to avoid contention overhead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 6,046 ms | 96 ms | **63x** |
| 10% | 5,866 ms | 1,750 ms | **3.4x** |
| 25% | 4,402 ms | 2,299 ms | **1.9x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 63x faster, but at 25% only 1.9x. The pattern mirrors SQLite: recursive subdivision to isolate failures dramatically increases the number of `SaveChanges` calls.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (6,046 → 4,402 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (96 → 2,299 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 9.6 MB | 15.0 MB |
| 10% | 26.3 MB | 13.8 MB |
| 25% | 28.6 MB | 12.2 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). If you expect frequent validation failures, consider pre-validating entities before calling the batch operation, or accept that DivideAndConquer will degrade to OneByOne-like performance for the affected batches.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| Any batch size | `Winnower` with `DivideAndConquer` |
| Experimenting with parallelism | `ParallelWinnower` with DOP 4 |

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
