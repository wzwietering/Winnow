# Options Reference

All batch options classes and their properties.

## WinnowOptions

Used with `Update`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `ValidateNavigationProperties` | `bool` | `true` | When true, validates navigation properties are not modified |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff for transient failures |
| `Validation` | `ValidationOptions?` | `null` | Pre-validation pipeline — set via `WithValidation<T>(...)` or `WithDataAnnotations<T>()`. Invalid entities are recorded as failures with `FailureReason.PreValidationError` and never reach the strategy. See [Pre-Validation](../pre-validation.md) |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. Lower levels reduce memory; see [ResultDetail](#resultdetail) |

All options classes that inherit from `WinnowOptions` also support `Strategy`, `ValidateNavigationProperties`, `Retry`, `Validation`, and `ResultDetail`. All options classes that inherit from `GraphOptionsBase` also support `Retry`, `Validation`, and `ResultDetail`.

> **Type note:** `WinnowOptions.Validation` is typed as `ValidationOptions?`. `GraphOptionsBase.Validation` is typed as `GraphValidationOptions?` (a `ValidationOptions` subclass that adds `IncludeNavigations` / `MaxNavigationDepth`). The two property types are different so navigation walking is unreachable from a flat operation by construction.

## InsertOptions

Used with `Insert`. Inherits from `WinnowOptions`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |

## InsertGraphOptions

Used with `InsertGraph`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `IncludeReferences` | `bool` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `bool` | `false` | Include many-to-many navigations |
| `ManyToManyInsertBehavior` | `ManyToManyInsertBehavior` | `AttachExisting` | How to handle M2M related entities |
| `ValidateManyToManyEntitiesExist` | `bool` | `true` | Validate M2M entities exist |
| `CircularReferenceHandling` | `CircularReferenceHandling` | `Throw` | How to handle circular references |
| `ThrowOnUnsupportedValidation` | `bool` | `false` | Throws if M2M validation can't be performed for composite key entities |
| `MaxManyToManyCollectionSize` | `int` | `0` | Max M2M collection size (0 = no limit) |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff |
| `Validation` | `GraphValidationOptions?` | `null` | Pre-validation pipeline — set via `WithValidation<T>(...)` or `WithDataAnnotations<T>(includeNavigations: ...)`. See [GraphValidationOptions](#graphvalidationoptions) |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. See [ResultDetail](#resultdetail) |

## GraphOptions

Used with `UpdateGraph`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `OrphanedChildBehavior` | `OrphanBehavior` | `Throw` | What to do with removed children |
| `IncludeReferences` | `bool` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `bool` | `false` | Include many-to-many navigations |
| `CircularReferenceHandling` | `CircularReferenceHandling` | `Throw` | How to handle circular references |
| `MaxManyToManyCollectionSize` | `int` | `0` | Max M2M collection size (0 = no limit) |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff |
| `Validation` | `GraphValidationOptions?` | `null` | Pre-validation pipeline — set via `WithValidation<T>(...)` or `WithDataAnnotations<T>(includeNavigations: ...)`. See [GraphValidationOptions](#graphvalidationoptions) |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. See [ResultDetail](#resultdetail) |

## DeleteOptions

Used with `Delete`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |

## DeleteGraphOptions

Used with `DeleteGraph`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `CascadeBehavior` | `DeleteCascadeBehavior` | `Cascade` | How to handle children on delete |
| `IncludeReferences` | `bool` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `bool` | `false` | Clean up M2M join records |
| `CircularReferenceHandling` | `CircularReferenceHandling` | `Throw` | How to handle circular references |
| `ValidateReferencedEntitiesExist` | `bool` | `true` | Validate referenced entities exist before deletion |
| `MaxManyToManyCollectionSize` | `int` | `0` | Max M2M collection size (0 = no limit) |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff |
| `Validation` | `GraphValidationOptions?` | `null` | Pre-validation pipeline — set via `WithValidation<T>(...)` or `WithDataAnnotations<T>(includeNavigations: ...)`. See [GraphValidationOptions](#graphvalidationoptions) |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. See [ResultDetail](#resultdetail) |

## UpsertOptions

Used with `Upsert`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `DuplicateKeyStrategy` | `DuplicateKeyStrategy` | `Fail` | How to handle duplicate key errors during INSERT |
| `MatchBy` (configure via `WithMatchBy`) | `internal` | `null` | Business-key expression for insert/update routing. Set via `options.WithMatchBy<TEntity>(e => e.Sku)` (single property) or `options.WithMatchBy<TEntity>(e => new { e.TenantId, e.ExternalId })` (composite). See [Upsert Operations → Custom Match Expressions](../upsert-operations.md#custom-match-expressions-matchby). |

Note: `MatchBy` rejects entity types that have a `HasQueryFilter` defined — see the
linked docs for the rationale and mitigations.

## UpsertGraphOptions

Used with `UpsertGraph`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `OrphanedChildBehavior` | `OrphanBehavior` | `Throw` | What to do with removed children |
| `IncludeReferences` | `bool` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `bool` | `false` | Include many-to-many navigations |
| `ManyToManyInsertBehavior` | `ManyToManyInsertBehavior` | `AttachExisting` | How to handle M2M related entities |
| `ValidateManyToManyEntitiesExist` | `bool` | `true` | Validate M2M entities exist |
| `CircularReferenceHandling` | `CircularReferenceHandling` | `Throw` | How to handle circular references |
| `DuplicateKeyStrategy` | `DuplicateKeyStrategy` | `Fail` | How to handle duplicate key errors during INSERT |
| `ThrowOnUnsupportedValidation` | `bool` | `false` | Throws if M2M validation can't be performed for composite key entities |
| `MaxManyToManyCollectionSize` | `int` | `0` | Max M2M collection size (0 = no limit) |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff |
| `Validation` | `GraphValidationOptions?` | `null` | Pre-validation pipeline — set via `WithValidation<T>(...)` or `WithDataAnnotations<T>(includeNavigations: ...)`. See [GraphValidationOptions](#graphvalidationoptions) |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. See [ResultDetail](#resultdetail) |

## RetryOptions

Configure automatic retry with exponential backoff for transient failures. Available on all batch operations via the `Retry` property.

```csharp
var result = saver.Update(entities, new WinnowOptions
{
    Retry = new RetryOptions
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromMilliseconds(200)
    }
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts. Must be non-negative. |
| `InitialDelay` | `TimeSpan` | `100ms` | Delay before first retry. Subsequent retries use exponential backoff. |
| `BackoffMultiplier` | `double` | `2.0` | Multiplier applied to delay between retries. Must be positive. |
| `IsTransient` | `Func<Exception, bool>?` | `null` | Custom predicate for transient exception detection. When null, uses built-in classifier. |

> **Note:** The retry handler re-invokes `SaveChanges` on the same `DbContext`. For providers that do not automatically recover entity state after transient failures, consider using the provider's built-in retry strategy (e.g., `EnableRetryOnFailure`) instead.

## ValidationOptions

Configures the pre-validation pipeline that runs in-process before any database round trip. Attach via the `WithValidation<TEntity>(...)` or `WithDataAnnotations<TEntity>()` extension methods on any `WinnowOptions` (or `WinnowOptions`-derived) instance — never instantiate this type directly. See [Pre-Validation](../pre-validation.md) for the full usage guide.

```csharp
var options = new InsertOptions().WithDataAnnotations<Product>(ValidationFailureBehavior.Throw);
options.Validation!.CancellationCheckInterval = 64; // optional tuning
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FailureBehavior` | `ValidationFailureBehavior` | `RecordAsFailure` | What happens when at least one entity fails validation. See [ValidationFailureBehavior](#validationfailurebehavior). |
| `CancellationCheckInterval` | `int` | `256` | How often (in entities) the validation pipeline polls the cancellation token. Lower values give faster cancellation at a small throughput cost; must be positive. |

## GraphValidationOptions

Subclass of `ValidationOptions` used on graph operations (`InsertGraph`, `UpdateGraph`, `DeleteGraph`, `UpsertGraph`). Adds navigation-walk controls so the validator can descend into reachable child entities. Attach via the `WithValidation` / `WithDataAnnotations` extensions on a `GraphOptionsBase` subtype; `IncludeNavigations` requires the `WithDataAnnotations` form.

```csharp
var options = new InsertGraphOptions().WithDataAnnotations<CustomerOrder>(includeNavigations: true);
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FailureBehavior` | `ValidationFailureBehavior` | `RecordAsFailure` | Inherited from `ValidationOptions`. |
| `CancellationCheckInterval` | `int` | `256` | Inherited from `ValidationOptions`. |
| `IncludeNavigations` | `bool` | `false` | When `true`, descend into navigation properties and validate each reachable entity. Requires a DataAnnotations validator; setting this to `true` with a delegate-only validator throws `InvalidOperationException` at configuration time. The walk honours `GraphOptionsBase.NavigationFilter`. |
| `MaxNavigationDepth` | `int` | `32` | Recursion-depth cap for the navigation walk. When the cap is reached, a `ValidationError` with code `WINNOW_NAV_DEPTH_LIMIT` is recorded at the cut-off point. Must be positive. |

## Enums

### BatchStrategy

| Value | Description |
|-------|-------------|
| `OneByOne` | Process each entity individually. Predictable performance. |
| `DivideAndConquer` | Binary search for failures. Faster when failure rate < 5%. |

### OrphanBehavior

| Value | Description |
|-------|-------------|
| `Throw` | Exception if children are removed. Prevents accidental data loss. |
| `Delete` | Delete removed children from database. |
| `Detach` | Keep removed children in database (may violate FK constraints). |

### DeleteCascadeBehavior

| Value | Description |
|-------|-------------|
| `Cascade` | Delete children first, then parent. Always works. |
| `Throw` | Exception if parent has loaded children. |
| `ParentOnly` | Only delete parent, rely on database CASCADE DELETE. |

### CircularReferenceHandling

| Value | Description |
|-------|-------------|
| `Throw` | Exception on circular reference. Prevents infinite loops. |
| `Ignore` | Process each entity once, skip revisits. |
| `IgnoreAll` | Allow all patterns including direct self-references. |

### ManyToManyInsertBehavior

| Value | Description |
|-------|-------------|
| `AttachExisting` | Assume related entities exist in database. |
| `InsertIfNew` | Insert related entities if they have default key. |

### DuplicateKeyStrategy

| Value | Description |
|-------|-------------|
| `Fail` | Record in Failures collection (default). |
| `RetryAsUpdate` | Retry failed INSERT as UPDATE. Handles race conditions. |
| `Skip` | Skip silently without recording as failure. |

### ValidationFailureBehavior

| Value | Description |
|-------|-------------|
| `RecordAsFailure` | Default. Each invalid entity becomes a failure with `FailureReason.PreValidationError`. Valid entities in the same batch still hit the database. Matches the "winnow out the failures" model the rest of the library follows. |
| `Throw` | After validating every entity in the batch, throw a `WinnowValidationException` aggregating all failures. Valid entities in the same batch are not sent to the database — the throw pre-empts the entire round trip. The scan is not short-circuited on the first failure; every offending entity is included so callers can react to them all without re-running the validator. |

### FailureReason

| Value | Description |
|-------|-------------|
| `ValidationError` | Entity failed EF Core's save-time validation (not Winnow pre-validation). `failure.ValidationErrors` is always `null` for this reason — use `PreValidationError` when you need structured per-property errors. |
| `ConcurrencyConflict` | Optimistic concurrency conflict (row was modified). |
| `DatabaseConstraint` | Database constraint violation (FK, check, etc.). |
| `DuplicateKey` | Primary key or unique constraint violation. |
| `Cancelled` | Operation was cancelled via CancellationToken. |
| `UnknownError` | Unclassified error. |
| `BusinessKeyConflictLost` | Under `MatchBy` with `DuplicateKeyStrategy.RetryAsUpdate`, a concurrent process won a race: the row matching our business key existed long enough to cause our INSERT to fail, but was gone by the time we re-queried for the retry. The entity is not persisted; inspect `UpsertFailure.EntityIndex` and decide whether to discard or re-queue. |
| `PreValidationError` | Entity was rejected in-process by Winnow pre-validation before any database round trip. `failure.ValidationErrors` carries the structured per-property errors; drive UI / API responses off that list rather than parsing `ErrorMessage`. See [Pre-Validation](../pre-validation.md). |

### ResultDetail

Controls how much per-entity data the result captures. Numeric ordering is meaningful — `Full > Minimal > None`. Reducing detail does not change which rows are written, only what the result reports. `SuccessCount` and `FailureCount` remain accurate at every level.

Properties whose backing data was not captured throw `InvalidOperationException` rather than returning silently empty collections.

| Value | Description |
|-------|-------------|
| `Full` | Default. Captures inserted entity references, failure exceptions, graph hierarchy, and traversal statistics. |
| `Minimal` | Captures successful IDs and failure indices/messages. Drops entity object references, exception object references, graph hierarchy, and traversal statistics. |
| `None` | Captures only aggregate counts (`SuccessCount`, `FailureCount`, `TotalRetries`, `Duration`). |

| Property | Available at |
|----------|--------------|
| `SuccessCount`, `FailureCount`, `Duration`, `TotalRetries`, `WasCancelled`, `IsCompleteSuccess`/`IsCompleteFailure`/`IsPartialSuccess` | `Full`, `Minimal`, `None` |
| `SuccessfulIds`, `InsertedIds`, `UpdatedIds`, `FailedIds`, `Failures` | `Full`, `Minimal` |
| `Failures[i].Exception` | `Full` only (null at `Minimal`) |
| `InsertedEntities`, `UpdatedEntities`, `AllUpsertedEntities` | `Full` only |
| `GraphHierarchy`, `TraversalInfo` | `Full` only |

```csharp
// Full (default) — best for debugging or when entity refs are needed downstream
var result = saver.Insert(items, new InsertOptions { ResultDetail = ResultDetail.Full });
var entity = result.InsertedEntities[0].Entity;

// Minimal — drop entity refs and graph hierarchy; keep IDs and failure metadata
var result = saver.Insert(items, new InsertOptions { ResultDetail = ResultDetail.Minimal });
var ids = result.InsertedIds;
// result.InsertedEntities throws InvalidOperationException

// None — fastest path, counts only
var result = saver.Insert(items, new InsertOptions { ResultDetail = ResultDetail.None });
var n = result.SuccessCount;
// result.InsertedIds throws InvalidOperationException
```

> **Note:** `ResultDetail` controls reporting only. Correctness-side trackers (orphan deletion, many-to-many link change tracking) run at every level, so `OrphanBehavior.Delete` and `IncludeManyToMany` continue to work correctly when `ResultDetail` is `Minimal` or `None`.
