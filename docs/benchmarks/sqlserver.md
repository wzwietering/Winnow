# SQL Server Benchmarks

Performance measurements on SQL Server (via Docker) using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.14.0

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 18.9 ms | 20.0 ms | 1.06x | 733.9 ms | 38.8x |
| 1,000 | 100.4 ms | 100.0 ms | **1.00x** | 6,935.0 ms | 69.1x |
| 5,000 | 323.1 ms | 244.4 ms | **0.76x** | 32,843.1 ms | 101.7x |

DivideAndConquer matches raw EF Core at 1,000 entities and is **24% faster** at 5,000 entities. Winnow's batching strategy appears to outperform raw `AddRange` + `SaveChanges` at larger scales, likely because dividing into smaller `SaveChanges` calls reduces SQL Server's per-statement overhead. Winnow adds ~30-38% memory overhead for result tracking and metadata.

OneByOne is 39-102x slower than raw EF Core, confirming it should only be used when individual entity error isolation is required.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 19.5 ms | 94.9 ms | 252.8 ms | 400.5 ms |
| Insert O×O | 616.3 ms | 6,388.1 ms | 33,345.0 ms | 65,844.7 ms |
| Speedup | **32x** | **67x** | **132x** | **164x** |
| | | | | |
| **Update D&C** | 18.9 ms | 92.1 ms | 268.0 ms | 508.6 ms |
| Update O×O | 603.0 ms | 6,624.4 ms | 33,805.0 ms | 66,800.8 ms |
| Speedup | **32x** | **72x** | **126x** | **131x** |
| | | | | |
| **Upsert D&C** | 23.4 ms | 95.6 ms | 235.1 ms | 460.7 ms |
| Upsert O×O | 581.6 ms | 6,390.1 ms | 33,874.5 ms | 65,704.7 ms |
| Speedup | **25x** | **67x** | **144x** | **143x** |
| | | | | |
| **Delete D&C** | 14.7 ms | 67.7 ms | 188.6 ms | — |
| Delete O×O | 593.1 ms | 6,307.8 ms | 33,127.5 ms | — |
| Speedup | **40x** | **93x** | **176x** | — |

DivideAndConquer is 25-176x faster across every operation and batch size. The speedup increases with batch size, similar to SQLite but not as extreme. SQL Server's per-entity round-trip cost (~6.5 seconds per 1,000 entities for OneByOne) falls between PostgreSQL (~600 ms) and SQLite (~9,000 ms). Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~6.5 seconds per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~4.2x
- SQL Server D&C absolute times are similar to PostgreSQL, both significantly faster than SQLite at larger sizes

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.10 MB | 9.56 MB | 46.64 MB | 93.35 MB |
| Insert O×O | 1.61 MB | 14.96 MB | 74.11 MB | 148.52 MB |
| Reduction | 32% | 36% | 37% | 37% |
| | | | | |
| Update D&C | 1.10 MB | 10.90 MB | 52.99 MB | 106.07 MB |
| Delete D&C | 0.83 MB | 8.09 MB | 39.49 MB | — |

DivideAndConquer uses 32-37% less memory than OneByOne. At 10,000 entities the per-entity cost is ~9 KB (D&C) vs ~15 KB (OneByOne). SQL Server has the lowest per-entity memory cost of all three providers.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 108 ms | 310 ms | 1,868 ms |
| InsertGraph O×O | 917 ms | 10,001 ms | 49,288 ms |
| Speedup | **8.5x** | **32x** | **26x** |
| | | | |
| **UpsertGraph D&C** | 84 ms | 382 ms | 1,862 ms |
| UpsertGraph O×O | 849 ms | 9,228 ms | 45,619 ms |
| Speedup | **10x** | **24x** | **25x** |
| | | | |
| **DeleteGraph D&C** | 84 ms | 410 ms | 3,855 ms |
| DeleteGraph O×O | 795 ms | 8,880 ms | 41,792 ms |
| Speedup | **9.5x** | **22x** | **11x** |

Graph D&C speedup (8.5-32x) is higher than PostgreSQL (2.5-8.2x) but lower than SQLite (13-77x). DeleteGraph D&C at 5,000 roots is notably slower (3,855 ms) compared to InsertGraph (1,868 ms), suggesting SQL Server cascade operations add overhead at scale.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 489 MB | ~20 KB |
| UpsertGraph D&C | 728 MB | ~29 KB |
| DeleteGraph D&C | 547 MB | ~22 KB |
| Flat Insert D&C @ 5K (for comparison) | 47 MB | ~9 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~29 KB/entity because it loads existing entities, tracks changes, and traverses navigations.

## ParallelWinnower

Tests `ParallelWinnower` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 98 ms | 287 ms |
| 2 | 108 ms | **252 ms** |
| 4 | **75 ms** | 269 ms |
| 8 | 97 ms | 331 ms |

**Parallelism shows modest benefit at specific configurations.** At 1,000 entities, DOP 4 is 23% faster than DOP 1. At 5,000 entities, DOP 2 is 12% faster but DOP 8 is 15% slower. Results are inconsistent, suggesting that SQL Server's lock management and connection pool overhead offset the gains from parallel execution.

Memory is nearly identical across all DOP values (~10 MB @ 1K, ~47 MB @ 5K), confirming that partitioning does not affect total allocation.

For SQL Server, `ParallelWinnower` with DOP 2-4 may provide marginal benefit, but the improvement is not consistent enough to recommend over standard `Winnower` for most workloads.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 6,372 ms | 85 ms | **75x** |
| 10% | 5,531 ms | 1,869 ms | **3.0x** |
| 25% | 5,031 ms | 2,726 ms | **1.8x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 75x faster, but at 25% only 1.8x. The pattern mirrors SQLite: recursive subdivision to isolate failures dramatically increases the number of `SaveChanges` calls.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (6,372 → 5,031 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (85 → 2,726 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 9.8 MB | 14.9 MB |
| 10% | 26.2 MB | 13.7 MB |
| 25% | 28.4 MB | 12.0 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). If you expect frequent validation failures, consider pre-validating entities before calling the batch operation, or accept that DivideAndConquer will degrade to OneByOne-like performance for the affected batches.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| Any batch size | `Winnower` with `DivideAndConquer` |
| Experimenting with parallelism | `ParallelWinnower` with DOP 2-4 |

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
