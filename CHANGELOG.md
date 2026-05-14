# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Pre-validation pipeline — attach a `ValidatorDelegate<TEntity>` via `WithValidation<TEntity>(...)` (or the matching extension on a graph options type) to reject entities in-process before any database round trip. Invalid entities are recorded as failures with `FailureReason.ValidationError` and never reach the strategy, restoring `DivideAndConquer`'s speed advantage at moderate-to-high failure rates. See [docs/pre-validation.md](docs/pre-validation.md).
- Built-in `WithDataAnnotations<TEntity>()` adapter that drives pre-validation from `System.ComponentModel.DataAnnotations` attributes declared on the entity. Per-type reflection cost is paid once at cache build time; each annotated property's getter is compiled to a direct expression delegate so the per-entity hot path is a linear walk over `(compiled getter, ValidationAttribute[])` pairs with no `PropertyInfo.GetValue` reflection per entity.
- `ValidationOptions`, `ValidationError` (readonly record struct), `ValidationCollector` (ref struct with pooled buffer), `ValidationFailureBehavior` (`RecordAsFailure` / `Throw`), and `WinnowValidationException` carrying aggregated failures when `Throw` is selected. The failures-list constructor requires a non-empty list — an empty list is semantically incoherent and is rejected with `ArgumentException`. Standard .NET exception constructors `()`, `(string)`, and `(string, Exception)` are also provided for serialization and re-throw scenarios; those overloads expose an empty `Failures` collection. The per-entity record is nested as `WinnowValidationException.EntityFailure` rather than a top-level type, so the scope is obvious from the IntelliSense path.
- `GraphValidationOptions : ValidationOptions` exposes `IncludeNavigations` only on the graph path. The IDE prevents the misconfiguration of enabling navigation walking on a flat operation — `GraphOptionsBase.Validation` is typed as `GraphValidationOptions?`, so the `IncludeNavigations` knob is unreachable from `InsertOptions`/`DeleteOptions`/`UpsertOptions`.
- `IncludeNavigations` (graph operations only) descends into navigation properties and validates each reachable entity via DataAnnotations. Cycle protection is reference-based; failures are surfaced on the top-level entity with a property path locating the offending child. The walk honours `GraphOptionsBase.NavigationFilter` (excluded navigations are skipped) and recurses through unannotated intermediate types so `Root → Mid (no annotations) → Leaf [Required]` still surfaces leaf failures. The walker stops at `GraphValidationOptions.MaxNavigationDepth` (default 32) and records a `WINNOW_NAV_DEPTH_LIMIT` validation error at the cut-off point, replacing the previous risk of `StackOverflowException` on accidentally-unbounded or deeply-nested graphs. The `IncludeNavigations = true` setter now eagerly rejects the combination with a non-DataAnnotations validator at configuration time, rather than throwing once the first batch is in flight.
- `ValidationErrors` (`IReadOnlyList<ValidationError>?`) on `InsertFailure`, `WinnowFailure<TKey>`, and `UpsertFailure<TKey>` — the structured per-property errors recorded by pre-validation, populated only when `Reason == FailureReason.ValidationError`. Drive UI / API responses off this list rather than parsing `ErrorMessage`.
- Fluent extension methods `WithValidation<TEntity>` / `WithDataAnnotations<TEntity>` provide one overload per concrete options type (`InsertOptions`, `DeleteOptions`, `UpsertOptions`, `InsertGraphOptions`, `GraphOptions`, `DeleteGraphOptions`, `UpsertGraphOptions`, plus the `WinnowOptions` base) and return the receiver's exact type — `new InsertOptions { Strategy = ... }.WithDataAnnotations<Order>()` keeps the `InsertOptions` type through the chain. Every overload accepts an optional `ValidationFailureBehavior onFailure` parameter so `Throw` mode is reachable in a single call; the graph `WithDataAnnotations` overloads additionally accept `bool includeNavigations` so the navigation-walk combination is set up safely in one call as well.
- `Upsert` with `WithMatchBy` + `WithValidation` combined: the MatchBy resolution now indexes by the entity's original input position, so pre-validation rejecting an entity does not break per-entity routing for the remaining survivors. `RejectDuplicateMatchKeys` reports duplicates using the caller's original input indices.
- `ValidationCollector.CreateForTesting()` — public factory so external consumers can exercise their own `ValidatorDelegate<TEntity>` in unit tests without reaching for the internal constructor. `Dispose()` is `public` so the supported `using var c = ValidationCollector.CreateForTesting();` pattern works from outside the assembly — required when a test may push more than `InlineCapacity` (4) errors so the rented `ArrayPool<T>` buffer is returned rather than leaked.
- `ParallelWinnower` preserves `ValidationErrors` across partition merges. Previously, pre-validation failures observed through `ParallelWinnower<TEntity, TKey>` returned `failure.ValidationErrors == null` because the parallel result merger reconstructed `InsertFailure` / `UpsertFailure<TKey>` without copying that collection. Fixed in this release; the single-process `Winnower` path was already correct.
- `PreValidationBenchmarks` mirroring `FailureRateBenchmarks`'s entity shape so pre-validation overhead and recovered throughput compare directly against the existing failure-rate table.

## [1.2.0] - 2026-05-14

### Added

- `UpsertOptionsExtensions.WithMatchBy<TEntity>` — route upsert by a business-key expression (single property or anonymous projection for composite keys) instead of the primary-key default-value check. See [docs/upsert-operations.md](docs/upsert-operations.md#custom-match-expressions-matchby).
- `UpsertResult.InsertedWithNullMatchKeyCount` (`int?`) — count of entities routed to INSERT because their MatchBy projection contained a null component. `null` when MatchBy was not configured.
- `FailureReason.BusinessKeyConflictLost` — `DuplicateKeyStrategy.RetryAsUpdate` under MatchBy could not refresh the row at retry time (concurrent writer won the race).

### Changed

- `DuplicateKeyStrategy.RetryAsUpdate` re-queries by business key when MatchBy is configured, copying the existing row's primary key and concurrency tokens before re-issuing as UPDATE.

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
