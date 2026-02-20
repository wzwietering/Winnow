# SQLite Benchmarks

Performance measurements on SQLite using BenchmarkDotNet. All benchmarks use a flat `BenchmarkProduct` entity unless noted otherwise. Graph benchmarks use a 3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each), totalling 5 entities per root.

**Environment:** Windows 11, Intel Core Ultra 5 225U (12 cores), .NET 10.0.3, BenchmarkDotNet v0.14.0

## Raw EF Core vs Library

Compares `context.AddRange(); context.SaveChanges()` against the library's strategies.

| BatchSize | Raw EF Core | D&C | Ratio | OneByOne | Ratio |
|-----------|-------------|-----|-------|----------|-------|
| 100 | 13.9 ms | 8.4 ms | **0.61x** | 218.8 ms | 15.7x |
| 1,000 | 54.4 ms | 67.2 ms | 1.24x | 3,261.7 ms | 59.9x |
| 5,000 | 117.2 ms | 106.7 ms | **0.91x** | 13,407.9 ms | 114.4x |

DivideAndConquer matches or beats raw EF Core at every tested size. At 100 entities it's 39% faster; at 5,000 it's 9% faster. The library adds ~27-30% memory overhead for result tracking and metadata.

OneByOne is 15-114x slower than raw EF Core, confirming it should only be used when individual entity error isolation is required.

## Flat Operations

### Speed: DivideAndConquer vs OneByOne

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| **Insert D&C** | 6.5 ms | 70.6 ms | 134.6 ms | 178.6 ms |
| Insert O×O | 357.5 ms | 2,534.6 ms | 13,090.3 ms | 25,410.8 ms |
| Speedup | **55x** | **36x** | **97x** | **142x** |
| | | | | |
| **Update D&C** | 9.2 ms | 73.0 ms | 109.3 ms | 163.1 ms |
| Update O×O | 215.1 ms | 2,320.6 ms | 12,636.3 ms | 25,437.8 ms |
| Speedup | **23x** | **32x** | **116x** | **156x** |
| | | | | |
| **Upsert D&C** | 6.6 ms | 67.8 ms | 111.4 ms | 167.5 ms |
| Upsert O×O | 228.0 ms | 2,710.9 ms | 13,102.8 ms | 25,518.0 ms |
| Speedup | **35x** | **40x** | **118x** | **152x** |
| | | | | |
| **Delete D&C** | 6.6 ms | 57.2 ms | 65.1 ms | — |
| Delete O×O | 230.9 ms | 2,611.3 ms | 12,103.6 ms | — |
| Speedup | **35x** | **46x** | **186x** | — |

DivideAndConquer is 23-186x faster across every operation and batch size. The speedup increases with batch size, making it even more advantageous at scale. Delete is the fastest operation at every size.

### Scaling Characteristics

- **OneByOne** scales linearly: ~2.5 seconds per 1,000 entities regardless of operation type
- **DivideAndConquer** scales sub-linearly: going from 1,000 to 10,000 entities (10x data) only increases time ~2.4x
- The scaling ceiling has not been reached at 10,000 entities

### Memory

| Operation | 100 | 1,000 | 5,000 | 10,000 |
|-----------|-----|-------|-------|--------|
| Insert D&C | 1.18 MB | 11.32 MB | 54.13 MB | 108.42 MB |
| Insert O×O | 1.83 MB | 17.89 MB | 89.32 MB | 178.65 MB |
| Reduction | 35% | 37% | 39% | 39% |
| | | | | |
| Update D&C | 1.23 MB | 12.14 MB | 57.98 MB | 116.05 MB |
| Delete D&C | 0.97 MB | 9.52 MB | 46.08 MB | — |

DivideAndConquer uses 35-39% less memory than OneByOne. At 10,000 entities the per-entity cost is ~11 KB (D&C) vs ~18 KB (OneByOne), dominated by EF Core's change tracker.

## Graph Operations

3-level hierarchy: `Order` → `OrderItem` (x2) → `OrderReservation` (x1 each). Batch size refers to root entities; total entity count is 5x larger.

### Speed

| Operation | 100 roots (500 ent.) | 1,000 roots (5K ent.) | 5,000 roots (25K ent.) |
|-----------|---------------------|----------------------|------------------------|
| **InsertGraph D&C** | 67 ms | 195 ms | 811 ms |
| InsertGraph O×O | 262 ms | 3,324 ms | 17,611 ms |
| Speedup | **3.9x** | **17x** | **22x** |
| | | | |
| **UpsertGraph D&C** | 69 ms | 199 ms | 903 ms |
| UpsertGraph O×O | 253 ms | 2,428 ms | 16,452 ms |
| Speedup | **3.7x** | **12x** | **18x** |
| | | | |
| **DeleteGraph D&C** | 65 ms | 131 ms | 716 ms |
| DeleteGraph O×O | 242 ms | 3,375 ms | 15,996 ms |
| Speedup | **3.7x** | **26x** | **22x** |

Graph D&C speedup (4-26x) is lower than flat operations (23-186x). Navigation property traversal and entity attachment overhead compresses the advantage. DeleteGraph is the fastest graph operation.

### Memory

| Operation @ 5K roots (25K entities) | Allocated | Per entity |
|--------------------------------------|-----------|------------|
| InsertGraph D&C | 539 MB | ~22 KB |
| UpsertGraph D&C | 769 MB | ~31 KB |
| DeleteGraph D&C | 581 MB | ~23 KB |
| Flat Insert D&C @ 5K (for comparison) | 54 MB | ~11 KB |

Graph operations use 2-3x more memory per entity than flat operations. UpsertGraph is the most expensive at ~31 KB/entity because it loads existing entities, tracks changes, and traverses navigations. Plan memory capacity accordingly for large graph batches.

## ParallelBatchSaver

Tests `ParallelBatchSaver` async insert with DivideAndConquer at varying degrees of parallelism (DOP).

| DOP | 1,000 entities | 5,000 entities |
|-----|----------------|----------------|
| 1 | **56 ms** | **128 ms** |
| 2 | 75 ms | 170 ms |
| 4 | 77 ms | 164 ms |
| 8 | 82 ms | 121 ms |

**Parallelism provides no benefit on SQLite.** SQLite uses file-level locking, so parallel writes contend rather than parallelize. Multiple `DbContext` instances add coordination overhead without throughput gains.

Memory is nearly identical across all DOP values (~11.5 MB @ 1K, ~55 MB @ 5K), confirming that partitioning does not affect total allocation.

ParallelBatchSaver is designed for network databases (PostgreSQL, SQL Server) where concurrent connections can execute truly parallel I/O. For SQLite, use `BatchSaver` with DivideAndConquer instead.

## Failure Rate Impact

Tests how invalid entities (triggering `SaveChanges` exceptions) impact performance. Uses 1,000 entities with varying percentages of invalid products.

| Failure Rate | OneByOne | D&C | D&C Speedup |
|--------------|----------|-----|-------------|
| 0% | 2,624 ms | 52 ms | **51x** |
| 10% | 2,511 ms | 722 ms | **3.5x** |
| 25% | 1,902 ms | 1,030 ms | **1.8x** |

DivideAndConquer's advantage decreases sharply under failures. At 0% failures it's 51x faster, but at 25% only 1.8x. The binary split strategy must recursively subdivide batches to isolate each failing entity, increasing the number of `SaveChanges` calls.

The strategies react to failures in opposite ways:

- **OneByOne gets faster** with more failures (2,624 → 1,902 ms) because failed entities roll back quickly
- **DivideAndConquer gets slower** (52 → 1,030 ms) because of recursive subdivision to isolate failures

Memory follows the same pattern:

| Failure Rate | D&C Memory | OneByOne Memory |
|--------------|------------|-----------------|
| 0% | 11.3 MB | 17.9 MB |
| 10% | 27.7 MB | 16.4 MB |
| 25% | 29.7 MB | 14.2 MB |

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
dotnet run -c Release --project benchmarks/EfCoreUtils.Benchmarks -- --sqlite-only

# Specific benchmark class
dotnet run -c Release --project benchmarks/EfCoreUtils.Benchmarks -- --sqlite-only --filter '*BaselineInsertBenchmarks*'

# All providers (requires Docker for PostgreSQL/SQL Server)
dotnet run -c Release --project benchmarks/EfCoreUtils.Benchmarks
```

Results are written to `benchmarks/EfCoreUtils.Benchmarks/BenchmarkDotNet.Artifacts/results/`.
