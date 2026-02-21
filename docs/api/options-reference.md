# Options Reference

All batch options classes and their properties.

## BatchOptions

Used with `UpdateBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `ValidateNavigationProperties` | `bool` | `true` | When true, validates navigation properties are not modified |

## InsertBatchOptions

Used with `InsertBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |

## InsertGraphBatchOptions

Used with `InsertGraphBatch`.

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

## GraphBatchOptions

Used with `UpdateGraphBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `OrphanedChildBehavior` | `OrphanBehavior` | `Throw` | What to do with removed children |
| `IncludeReferences` | `bool` | `false` | Include many-to-one references |
| `IncludeManyToMany` | `bool` | `false` | Include many-to-many navigations |
| `CircularReferenceHandling` | `CircularReferenceHandling` | `Throw` | How to handle circular references |

## DeleteBatchOptions

Used with `DeleteBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |

## DeleteGraphBatchOptions

Used with `DeleteGraphBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `MaxDepth` | `int` | `10` | Maximum traversal depth |
| `NavigationFilter` | `NavigationFilter?` | `null` | Filter which navigations are traversed |
| `CascadeBehavior` | `DeleteCascadeBehavior` | `Cascade` | How to handle children on delete |
| `IncludeManyToMany` | `bool` | `false` | Clean up M2M join records |

## UpsertBatchOptions

Used with `UpsertBatch`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `BatchStrategy` | `OneByOne` | `OneByOne` or `DivideAndConquer` |
| `DuplicateKeyStrategy` | `DuplicateKeyStrategy` | `Fail` | How to handle duplicate key errors during INSERT |

## UpsertGraphBatchOptions

Used with `UpsertGraphBatch`.

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
