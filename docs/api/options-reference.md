# Options Reference

All batch options classes and their properties.

## WinnowOptions

Used with `Update`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `ValidateNavigationProperties` | `bool` | `true` | When true, validates navigation properties are not modified |
| `Retry` | `RetryOptions?` | `null` | Enables automatic retry with exponential backoff for transient failures |
| `ResultDetail` | `ResultDetail` | `Full` | How much per-entity detail the result captures. Lower levels reduce memory; see [ResultDetail](#resultdetail) |

All options classes that inherit from `WinnowOptions` also support `Strategy`, `ValidateNavigationProperties`, `Retry`, and `ResultDetail`. All options classes that inherit from `GraphOptionsBase` also support `Retry` and `ResultDetail`.

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

## UpsertOptions

Used with `Upsert`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `DuplicateKeyStrategy` | `DuplicateKeyStrategy` | `Fail` | How to handle duplicate key errors during INSERT |

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

### FailureReason

| Value | Description |
|-------|-------------|
| `ValidationError` | Entity failed validation. |
| `ConcurrencyConflict` | Optimistic concurrency conflict (row was modified). |
| `DatabaseConstraint` | Database constraint violation (FK, check, etc.). |
| `DuplicateKey` | Primary key or unique constraint violation. |
| `Cancelled` | Operation was cancelled via CancellationToken. |
| `UnknownError` | Unclassified error. |

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
