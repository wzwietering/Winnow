# SQLite Benchmarks

Performance measurements on SQLite using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.14.0

## Raw EF Core vs Winnow

Compares `context.AddRange(); context.SaveChanges()` against Winnow's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 16.3 ms | 17.2 ms | 1.06x | 1,067.9 ms | 65.5x |
| 1,000 | 69.0 ms | 75.5 ms | 1.09x | 10,572.9 ms | 153.2x |
| 5,000 | 127.5 ms | 136.2 ms | 1.07x | 47,641.7 ms | 373.6x |

DivideAndConquer adds 6-9% overhead compared to raw EF Core while providing error isolation and result tracking. Winnow adds ~27-30% memory overhead for result tracking and metadata.

OneByOne is 66-374x slower than raw EF Core, confirming it should only be used when individual entity error isolation is required.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 16.5 ms | 78.4 ms | 118.8 ms | 196.7 ms |
| Insert O×O | 770.4 ms | 8,967.0 ms | 45,012.8 ms | 94,327.4 ms |
| Speedup | **47x** | **114x** | **379x** | **480x** |
| | | | | |
| **Update D&C** | 17.6 ms | 96.0 ms | 121.4 ms | 172.8 ms |
| Update O×O | 802.8 ms | 9,272.4 ms | 45,896.3 ms | 92,265.3 ms |
| Speedup | **46x** | **97x** | **378x** | **534x** |
| | | | | |
| **Upsert D&C** | 14.1 ms | 25.7 ms | 123.1 ms | 172.9 ms |
| Upsert O×O | 783.0 ms | 9,061.5 ms | 46,945.6 ms | 89,504.5 ms |
| Speedup | **56x** | **353x** | **381x** | **518x** |
| | | | | |
| **Delete D&C** | 13.7 ms | 62.0 ms | 67.7 ms | — |
| Delete O×O | 756.0 ms | 8,716.6 ms | 44,620.5 ms | — |
| Speedup | **55x** | **141x** | **659x** | — |

DivideAndConquer is 46-659x faster across every operation and batch size. The speedup increases with batch size, making it even more advantageous at scale. Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~9 seconds per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~2.5x
- The scaling ceiling has not been reached at 10,000 entities

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.18 MB | 11.32 MB | 54.13 MB | 108.42 MB |
| Insert O×O | 1.83 MB | 17.89 MB | 89.32 MB | 179.03 MB |
| Reduction | 36% | 37% | 39% | 39% |
| | | | | |
| Update D&C | 1.23 MB | 11.63 MB | 57.61 MB | 115.68 MB |
| Delete D&C | 0.97 MB | 9.23 MB | 46.08 MB | — |

DivideAndConquer uses 36-39% less memory than OneByOne. At 10,000 entities the per-entity cost is ~11 KB (D&C) vs ~18 KB (OneByOne), dominated by EF Core's change tracker.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 100 ms | 219 ms | 871 ms |
| InsertGraph O×O | 1,280 ms | 11,046 ms | 55,951 ms |
| Speedup | **13x** | **50x** | **64x** |
| | | | |
| **UpsertGraph D&C** | 86 ms | 223 ms | 831 ms |
| UpsertGraph O×O | 1,424 ms | 10,438 ms | 54,144 ms |
| Speedup | **17x** | **47x** | **65x** |
| | | | |
| **DeleteGraph D&C** | 49 ms | 173 ms | 702 ms |
| DeleteGraph O×O | 984 ms | 10,943 ms | 54,016 ms |
| Speedup | **20x** | **63x** | **77x** |

Graph D&C speedup (13-77x) is lower than flat operations (46-659x). Navigation property traversal and entity attachment overhead compresses the advantage. DeleteGraph is the fastest graph operation.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 539 MB | ~22 KB |
| UpsertGraph D&C | 767 MB | ~31 KB |
| DeleteGraph D&C | 582 MB | ~23 KB |
| Flat Insert D&C @ 5K (for comparison) | 54 MB | ~11 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~31 KB/entity because it loads existing entities, tracks changes, and traverses navigations. Plan memory capacity accordingly for large graph batches.

## ParallelBatchSaver

Tests `ParallelBatchSaver` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | 105 ms | **143 ms** |
| 2 | **70 ms** | 164 ms |
| 4 | 104 ms | 253 ms |
| 8 | 137 ms | 179 ms |

**Parallelism provides no consistent benefit on SQLite.** SQLite uses file-level locking, so parallel writes contend rather than parallelize. The DOP 2 result at 1,000 entities appears faster but has high variance (StdDev 13 ms) and the advantage disappears at 5,000 entities.

Memory is nearly identical across all DOP values (~11.5 MB @ 1K, ~55 MB @ 5K), confirming that partitioning does not affect total allocation.

ParallelBatchSaver is designed for network databases (PostgreSQL, SQL Server) where concurrent connections can execute truly parallel I/O. For SQLite, use `BatchSaver` with DivideAndConquer instead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 9,538 ms | 76 ms | **125x** |
| 10% | 8,254 ms | 2,411 ms | **3.4x** |
| 25% | 6,538 ms | 3,388 ms | **1.9x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 125x faster, but at 25% only 1.9x. The binary split strategy must recursively subdivide batches to isolate each failing entity, increasing the number of `SaveChanges` calls.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (9,538 → 6,538 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (76 → 3,388 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 11.1 MB | 17.9 MB |
| 10% | 27.7 MB | 16.4 MB |
| 25% | 29.7 MB | 14.1 MB |

**Guidance:** Use DivideAndConquer when failure rates are low (under ~5%). If you expect frequent validation failures, consider pre-validating entities before calling the batch operation, or accept that DivideAndConquer will degrade to OneByOne-like performance for the affected batches.

## Choosing a Strategy

| Scenario | Recommended Strategy |
|----------|---------------------|
| General use, low failure rate | `DivideAndConquer` |
| Frequent validation failures (>10%) | Pre-validate, then `DivideAndConquer` |
| Need per-entity error isolation | `OneByOne` |
| SQLite | `BatchSaver` with `DivideAndConquer` |
| PostgreSQL / SQL Server with large batches | `ParallelBatchSaver` with `DivideAndConquer` |

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
