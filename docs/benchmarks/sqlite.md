# SQLite Benchmarks

Performance measurements on SQLite using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.15.8

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 14.1 ms | 14.9 ms | 1.06x | 681.4 ms | 48.3x |
| 1,000 | 75.9 ms | 89.7 ms | 1.18x | 9,047.9 ms | 119.2x |
| 5,000 | 140.9 ms | 109.9 ms | **0.78x** | 45,334.0 ms | 321.7x |

DivideAndConquer overhead is within measurement noise at most batch sizes — the error bars overlap with raw EF Core in all cases. Winnow adds ~27-30% memory overhead for result tracking and metadata.

OneByOne is 48-322x slower than raw EF Core, confirming it should only be used when individual entity error isolation is required.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 42.2 ms | 76.6 ms | 146.5 ms | 215.0 ms |
| Insert O×O | 822.5 ms | 8,656.4 ms | 43,586.7 ms | 88,276.9 ms |
| Speedup | **19x** | **113x** | **298x** | **411x** |
| | | | | |
| **Update D&C** | 17.0 ms | 51.6 ms | 146.2 ms | 179.9 ms |
| Update O×O | 697.3 ms | 9,744.2 ms | 43,684.8 ms | 88,452.3 ms |
| Speedup | **41x** | **189x** | **299x** | **492x** |
| | | | | |
| **Upsert D&C** | 12.9 ms | 79.3 ms | 115.0 ms | 166.9 ms |
| Upsert O×O | 688.1 ms | 9,245.4 ms | 45,015.5 ms | 88,400.5 ms |
| Speedup | **53x** | **117x** | **391x** | **530x** |
| | | | | |
| **Delete D&C** | 17.8 ms | 32.6 ms | 99.8 ms | — |
| Delete O×O | 782.8 ms | 10,259.6 ms | 43,571.4 ms | — |
| Speedup | **44x** | **315x** | **436x** | — |

DivideAndConquer is 19-530x faster across every operation and batch size. The speedup increases with batch size, making it even more advantageous at scale. Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~9 seconds per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~2.5x
- The scaling ceiling has not been reached at 10,000 entities

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.18 MB | 10.87 MB | 54.14 MB | 108.42 MB |
| Insert O×O | 1.83 MB | 17.99 MB | 89.82 MB | 179.26 MB |
| Reduction | 36% | 40% | 40% | 40% |
| | | | | |
| Update D&C | 1.23 MB | 11.63 MB | 57.99 MB | 116.06 MB |
| Delete D&C | 0.97 MB | 9.23 MB | 46.08 MB | — |

DivideAndConquer uses 36-40% less memory than OneByOne. At 10,000 entities the per-entity cost is ~11 KB (D&C) vs ~18 KB (OneByOne), dominated by EF Core's change tracker.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 66 ms | 227 ms | 763 ms |
| InsertGraph O×O | 957 ms | 10,045 ms | 53,716 ms |
| Speedup | **14x** | **44x** | **70x** |
| | | | |
| **UpsertGraph D&C** | 99 ms | 208 ms | 850 ms |
| UpsertGraph O×O | 906 ms | 10,103 ms | 51,137 ms |
| Speedup | **9x** | **49x** | **60x** |
| | | | |
| **DeleteGraph D&C** | 51 ms | 150 ms | 680 ms |
| DeleteGraph O×O | 907 ms | 11,026 ms | 51,981 ms |
| Speedup | **18x** | **74x** | **76x** |

Graph D&C speedup (9-76x) is lower than flat operations (19-530x). Navigation property traversal and entity attachment overhead compresses the advantage. DeleteGraph is the fastest graph operation.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 539 MB | ~22 KB |
| UpsertGraph D&C | 770 MB | ~31 KB |
| DeleteGraph D&C | 581 MB | ~23 KB |
| Flat Insert D&C @ 5K (for comparison) | 54 MB | ~11 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~31 KB/entity because it loads existing entities, tracks changes, and traverses navigations. Plan memory capacity accordingly for large graph batches.

## ParallelWinnower

Tests `ParallelWinnower` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 81 ms | **141 ms** |
| 2 | **77 ms** | 188 ms |
| 4 | 102 ms | 155 ms |
| 8 | 150 ms | 181 ms |

**Parallelism provides no consistent benefit on SQLite.** SQLite uses file-level locking, so parallel writes contend rather than parallelize. The DOP 2 result at 1,000 entities appears faster but has high variance and the advantage disappears at 5,000 entities.

Memory is nearly identical across all DOP values (~11.5 MB @ 1K, ~55 MB @ 5K), confirming that partitioning does not affect total allocation.

ParallelWinnower is designed for network databases (PostgreSQL, SQL Server) where concurrent connections can execute truly parallel I/O. For SQLite, use `Winnower` with DivideAndConquer instead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 9,056 ms | 60 ms | **151x** |
| 10% | 7,676 ms | 2,643 ms | **2.9x** |
| 25% | 6,676 ms | 3,794 ms | **1.8x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 151x faster, but at 25% only 1.8x. The binary split strategy must recursively subdivide batches to isolate each failing entity, increasing the number of `SaveChanges` calls.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (9,056 → 6,676 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (60 → 3,794 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 11.3 MB | 18.0 MB |
| 10% | 27.8 MB | 16.5 MB |
| 25% | 30.0 MB | 14.4 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). If you expect frequent validation failures, consider pre-validating entities before calling the batch operation, or accept that DivideAndConquer will degrade to OneByOne-like performance for the affected batches.

## ResultDetail

`ResultDetailBenchmarks` measures `ResultDetail.Full` (default), `Minimal`, and `None` for both flat `Insert` and `InsertGraph` at 1K and 5K entities. The savings differ sharply by workload:

- **Flat**: tracking adds ~27-30% over raw EF Core. `Minimal` drops entity refs and stats; `None` keeps only counts.
- **Graph**: tracking is the dominant cost (~22-31 KB/entity vs ~11 KB for flat — 2-3x). Most of that is the recursive `GraphHierarchy` tree, which only `Full` captures. `Minimal` and `None` skip building it.

Run the benchmark to measure for your workload:

```bash
dotnet run -c Release --project benchmarks/Winnow.Benchmarks -- --sqlite-only --filter '*ResultDetailBenchmarks*'
```

`SuccessCount` and `FailureCount` are accurate at every level. Properties whose data was not captured throw `InvalidOperationException` on access. Correctness-side trackers (orphan deletion, M2M change tracking) are unaffected by `ResultDetail`.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| SQLite | `Winnower` with `DivideAndConquer` |
| PostgreSQL / SQL Server with large batches | `ParallelWinnower` with `DivideAndConquer` |

## Running the Benchmarks

```bash
# SQLite only
dotnet run -c Release --project benchmarks/Winnow.Benchmarks -- --sqlite-only

# Specific benchmark class
dotnet run -c Release --project benchmarks/Winnow.Benchmarks -- --sqlite-only --filter '*BaselineInsertBenchmarks*'

# All providers (requires Docker for PostgreSQL/SQL Server)
dotnet run -c Release --project benchmarks/Winnow.Benchmarks
```

Results are written to `benchmarks/Winnow.Benchmarks/BenchmarkDotNet.Artifacts/results/`.
