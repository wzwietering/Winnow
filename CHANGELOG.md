# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `UpsertOptions.MatchBy` — optional business-key expression that overrides primary-key default-value detection with a custom lookup. Supports a single property (`e => e.ExternalId`) or anonymous composite projection (`e => new { e.TenantId, e.ExternalId }`). Winnow runs batched `SELECT`s (`AsNoTracking`, chunked to stay inside provider parameter limits) before `SaveChanges` to partition the input batch into insert/update sets. On update, the resolved row's primary key and concurrency-token values are copied onto the input entity so the subsequent `Modified` flip generates a correct UPDATE under optimistic concurrency. The setter is `internal` — assign via `WithMatchBy` to keep shape validation on the configuration path. `MatchBy` rejects primary-key and store-generated columns (computed, row-version, identity) at parse time with `ArgumentException` naming the offending property. Graph upsert does not support MatchBy; the property is exposed on `UpsertOptions` only.
- `UpsertOptionsExtensions.WithMatchBy<TEntity, TKey>` — strongly-typed fluent helper. Performs eager shape validation: invalid expression shapes (method calls, nested member access, complex projections) throw `ArgumentException` at the call site.
- `UpsertOptionsExtensions.WithMatchBy<TEntity>` — single-type-argument overload for composite keys, removing the awkward `TKey = object` requirement when supplying an anonymous projection.
- `UpsertResult.NullMatchKeyInsertCount` — count of entities routed to INSERT because their `MatchBy` projection contained a null component. A non-zero value typically indicates a data-quality issue upstream; callers can surface it without auditing every inserted entity. Zero when `MatchBy` is not configured.
- `FailureReason.MatchByRefreshNotFound` — recorded when `DuplicateKeyStrategy.RetryAsUpdate` fires under `MatchBy` and the refresh `SELECT` finds no matching row (e.g. the conflicting row was deleted between the original INSERT failure and the retry). Replaces the previous behavior of attempting a no-op UPDATE against the default primary key and surfacing a misleading classification.
- `DuplicateKeyStrategy.RetryAsUpdate` is MatchBy-aware: when a concurrent INSERT lands between the pre-SELECT and our save, the retry path re-queries by business key (via async I/O when called from `UpsertAsync`), copies the now-existing row's primary key + concurrency tokens, and re-issues as UPDATE.

### Notes

- Ambiguous matches (multiple existing rows for the same match key) and duplicate match keys within a single input batch are rejected with `InvalidOperationException` — Winnow does not silently pick a winner. Duplicate match keys whose values contain null are skipped (treated as "no business key") rather than rejected.

## [1.1.0] - 2026-05-08

### Added

- `ResultDetail` enum (`None`, `Minimal`, `Full`) and matching `WinnowOptions.ResultDetail` / `GraphOptionsBase.ResultDetail` properties to control how much per-entity detail batch results capture. Lower levels suppress reporting-only collections (entity references, exception object references, graph hierarchy, traversal statistics) and reduce memory for large batches. `SuccessCount`, `FailureCount`, `Duration`, and `TotalRetries` remain accurate at every level.
- Internal accumulator infrastructure (`InsertAccumulator`, `WinnowAccumulator`, `UpsertAccumulator`, `GraphResultAccumulator`, `AccumulatorFactory`) that centralises allocation gating; the accumulator pattern replaces ad-hoc bool flags previously scattered across operation classes.
- New `ResultDetailBenchmarks` quantifying allocation reduction at each detail level.

### Changed

- Result accessors now throw `InvalidOperationException` when their data was suppressed by a lower `ResultDetail` than they require. Default behaviour is preserved (`ResultDetail.Full`), so existing callers see no change. The exception message names the required level, the configured level, and the always-available alternative property where one exists.
- `UpsertResult.GetByIndex` now uses an O(1) dictionary cache (previously an O(n) linear scan over a concatenated list).

### Performance

- Memory savings of roughly 17–19% on `InsertGraph` workloads when callers opt into `ResultDetail.Minimal` or `ResultDetail.None`, by skipping the recursive `GraphHierarchy` tree and `TraversalInfo` statistics. Flat operations see negligible change since EF Core's change tracker dominates allocation there. See the per-provider benchmark docs for numbers.

## [1.0.0]

### Added

- Batch insert, update, delete, and upsert operations with per-entity failure isolation
- DivideAndConquer and OneByOne strategies for different failure rate scenarios
- Graph operations for parent-child entity hierarchies (InsertGraph, UpdateGraph, DeleteGraph, UpsertGraph)
- Parallel batch processing via ParallelWinnower with configurable degree of parallelism
- Composite key support via CompositeKey struct
- Many-to-many relationship handling with configurable insert behavior
- Reference navigation (many-to-one) support in graph operations
- Self-referencing hierarchy support with circular reference detection
- Navigation filtering to control which relationships are traversed
- Orphan behavior configuration (Delete, Ignore, Throw) for graph updates
- Upsert with duplicate key retry strategies (RetryAsUpdate, Skip, Throw)
- Retry options with configurable transient error detection
- Dependency injection integration via AddWinnow extension method
- DbContextFactory extensions for creating Winnower and ParallelWinnower instances
- Detailed result types with success/failure counts, duration, and database round-trip metrics
- Support for .NET 8.0, 9.0, and 10.0
