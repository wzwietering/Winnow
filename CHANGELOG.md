# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0]

### Breaking changes (soft)

- **New `FailureReason.PreValidationError` enum value.** The addition is binary-additive, but consumers with exhaustive `switch` expressions over `FailureReason` compiled with warning-as-error on `CS8509` will need to add a `default` arm or an explicit case for `PreValidationError`. No runtime behaviour changes for existing reasons.

### Added

- **Pre-validation pipeline.** Attach a `WinnowValidator<TEntity>` via `WithValidation<TEntity>(...)`, or wire DataAnnotations via `WithDataAnnotations<TEntity>()`, on any options object to reject entities in-process before any database round trip. Invalid entities become failures with `FailureReason.PreValidationError` and never reach the strategy, restoring `DivideAndConquer`'s speed advantage at moderate-to-high failure rates. New public surface: `ValidationOptions`, `ValidationError` (readonly record struct), `ValidationCollector` (allocation-light ref struct with pooled buffer), `ValidationFailureBehavior` (`RecordAsFailure` / `Throw`), `WinnowValidationException`, `WinnowEntityFailure`, and the `ValidationOptionsExtensions` / `GraphValidationOptionsExtensions` fluent helpers. See [docs/pre-validation.md](docs/pre-validation.md).
- **DataAnnotations adapter.** `WithDataAnnotations<TEntity>()` builds the validator from `System.ComponentModel.DataAnnotations` attributes on the entity. Per-type reflection cost is paid once and cached; per-entity hot path is a linear walk over compiled expression-tree getters with no `PropertyInfo.GetValue` reflection. Class-level `ValidationAttribute`s and `IValidatableObject.Validate(...)` are also invoked; `IValidatableObject` errors carry code `WINNOW_VALIDATABLE_OBJECT`.
- **Navigation walking for graph operations.** `GraphValidationOptions.IncludeNavigations` (default `false`) descends into navigation properties and validates each reachable entity via DataAnnotations. Cycle protection is reference-based; the walker honours `GraphOptionsBase.NavigationFilter` and stops at `MaxNavigationDepth` (default 32), recording a `WINNOW_NAV_DEPTH_LIMIT` error at the cut-off rather than risking `StackOverflowException`. The knob is unreachable from flat options by design — `GraphOptionsBase.Validation` is typed as `GraphValidationOptions?` while `WinnowOptions.Validation` is typed as `ValidationOptions?`. Setting `IncludeNavigations = true` requires a DataAnnotations-built validator and is rejected eagerly at configuration time otherwise.
- **Structured failure details.** New `ValidationErrors` (`IReadOnlyList<ValidationError>?`) property on `InsertFailure`, `WinnowFailure<TKey>`, and `UpsertFailure<TKey>`. Populated only when the failure was produced by pre-validation; drive UI / API responses off this list rather than parsing `ErrorMessage`.
- **`Upsert` with `WithMatchBy` + `WithValidation` combined.** MatchBy resolution now indexes by the entity's original input position, so pre-validation rejecting an entity does not break per-entity routing for the remaining survivors. `RejectDuplicateMatchKeys` reports duplicates using original input indices.
- **`ValidationCollector.Create()`** public factory and public `Dispose()` so external test code can exercise its own `WinnowValidator<TEntity>` via `using var c = ValidationCollector.Create();` without leaking the rented `ArrayPool<T>` buffer.
- **`PreValidationBenchmarks`** mirroring `FailureRateBenchmarks`'s entity shape, so pre-validation overhead and recovered throughput compare directly against the existing failure-rate table.

### Changed

- **Parallel + `ValidationFailureBehavior.Throw` now isolates the offender.** Previously, a single invalid entity in a partition caused `ParallelWinnower` to mark every entity in that partition as failed. The orchestrator now catches the partition's `WinnowValidationException`, records only the offending entities as `PreValidationError` failures, and re-runs the partition with the survivors so they reach the database. Behaviour now matches the single-context `Winnower` path.

### Fixed

- **`FailureCount` accuracy at `ResultDetail.None`** under `Throw` recovery. `BuildInsertValidationFailures` (and the matching `MergeWinnow` / `MergeUpsert` paths) returned an empty list at `None`, and the merger then computed `FailureCount = survivor + 0` — silently dropping the count of validation-rejected entities. The mergers now derive the count from the raw failure list rather than the gated collection's length.
- **`WINNOW_NAV_DEPTH_LIMIT` false positive at leaf-at-max-depth.** The navigation walker emitted a spurious depth-limit error for entities at exactly `MaxNavigationDepth` that had no further traversable navigations. It now checks for traversable navigations before recording the cut-off.
- **`ParallelWinnower` preserves `ValidationErrors` across partition merges.** Pre-validation failures observed through `ParallelWinnower<TEntity, TKey>` previously returned `failure.ValidationErrors == null` because the parallel result merger reconstructed the failure objects without copying the collection. The single-context `Winnower` path was already correct.
- **Parallel `Throw` recovery attributes results to the correct entity.** After the orchestrator caught a partition's `WinnowValidationException` and re-ran the survivor list, the strategy re-indexed survivors `0..N-1` and `ResultMerger` then added the partition offset — so `InsertedEntity.OriginalIndex` / `InsertFailure.EntityIndex` / `UpsertFailure.EntityIndex` could point at the wrong original entity. The orchestrator now carries a survivor-position → partition-position map and remaps result indices before the top-level merge.
- **Parallel `Throw` recovery for upsert reports `AttemptedOperation` and `IsDefaultKey` correctly.** The recovery path hard-coded `AttemptedOperation = Insert` and `IsDefaultKey = false` for every validation-failed entity in the parallel case; the single-context path was already correct. The parallel path now reads the primary-key CLR properties (reflection-only, no tracker attach) to classify failures the same way.
- **`ValidationResultMerger` no longer swallows programmer errors during key reads.** The `SafeReadKey` / `ClassifyUpsertFailure` paths on parallel `Throw` recovery caught all exceptions and collapsed them to default keys, hiding bugs like `NullReferenceException`. Suppression is now narrowed to `InvalidOperationException` instances originating in `Microsoft.EntityFrameworkCore` (matching the single-context `SuppressKeyReadFailures` pattern), so programmer errors surface as real bugs.

### Performance

- **`UpsertResult.GetFailureByIndex`** is now O(1) after first call via a lazy dictionary cache, matching the existing `GetByIndex` pattern (previously an O(n) linear scan over the failure list).

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
