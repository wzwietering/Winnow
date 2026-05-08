# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
