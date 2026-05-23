# Pre-Validation

Winnow's `DivideAndConquer` strategy is fast — until invalid entities show up. At 0% failures, D&C is 151× faster than `OneByOne` on SQLite. At 25% failures, it collapses to 1.8× because the recursion has to binary-split every batch that contains a failure, each split costing one database round trip.

Pre-validation lets you reject invalid entities in-process, **before** the strategy is invoked. The strategy then only sees valid entities, so its sub-linear scaling holds across the full failure-rate spectrum.

## Quick Start

```csharp
var options = new InsertOptions { Strategy = BatchStrategy.DivideAndConquer };
options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
{
    if (p.Price <= 0)
        c.Add(nameof(Product.Price), "Must be positive");
    if (string.IsNullOrWhiteSpace(p.Sku))
        c.Add(nameof(Product.Sku), "Required");
});

var saver = new Winnower<Product, int>(context);
var result = saver.Insert(products, options);

// Invalid entities show up in result.Failures with FailureReason.ValidationError.
foreach (var failure in result.Failures.Where(f => f.Reason == FailureReason.ValidationError))
{
    _logger.LogWarning("Index {Index}: {Message}", failure.EntityIndex, failure.ErrorMessage);
}
```

The delegate receives a `ref ValidationCollector` — a `ref struct` buffer with a 4-slot inline capacity. Emit zero, one, or many `ValidationError` instances per entity. The pipeline allocates one 4-slot buffer per batch (not per entity) and reuses it; the collector only rents from `ArrayPool<T>.Shared` when a single entity emits more than the inline capacity.

## DataAnnotations Adapter

If your entities already carry `[Required]`, `[Range]`, `[StringLength]`, or similar attributes, Winnow can drive validation from them directly:

```csharp
public class Product
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Sku { get; set; } = string.Empty;

    [Range(0.01, 999999.99)]
    public decimal Price { get; set; }
}

var options = new InsertOptions();
options.WithDataAnnotations<Product>();
```

The first call for a given entity type reflects over its properties to discover attributes and compiles a getter expression for each annotated property; subsequent calls reuse the cached array of compiled getters, so neither reflection nor `PropertyInfo.GetValue` runs per entity.

The adapter also runs:

- **Class-level `ValidationAttribute`s** (e.g. `[CustomValidation(typeof(MyValidator), nameof(MyValidator.Check))]` placed on the entity itself) via `attribute.GetValidationResult(entity, context)`. The error code is the attribute type name, matching property-level attributes.
- **`IValidatableObject.Validate(ValidationContext)`**, when the entity implements it — useful for cross-field rules that don't fit a single attribute. Errors are emitted with code `WINNOW_VALIDATABLE_OBJECT` so they can be distinguished from attribute-driven failures. Property paths come from `ValidationResult.MemberNames`.

Both surfaces are discovered once per entity type and cached.

```csharp
public class Booking : IValidatableObject
{
    [Required] public DateTime Start { get; set; }
    [Required] public DateTime End { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (End <= Start)
            yield return new ValidationResult("End must be after Start.", new[] { nameof(End) });
    }
}
```

`Booking`'s property-level `[Required]` attributes and the cross-field rule are reported in the same `ValidationCollector` and surface in `failure.ValidationErrors` together.

## FluentValidation Adapter

Winnow does not ship a built-in FluentValidation integration to keep its dependency surface minimal. Wrap your validator in a delegate:

```csharp
public sealed class ProductValidator : AbstractValidator<Product> { /* … */ }

ValidatorDelegate<Product> Wrap(ProductValidator fv) =>
    (Product p, ref ValidationCollector c) =>
    {
        var result = fv.Validate(p);
        if (result.IsValid) return;
        foreach (var failure in result.Errors)
            c.Add(failure.PropertyName, failure.ErrorMessage, failure.ErrorCode);
    };

var options = new InsertOptions();
options.WithValidation(Wrap(new ProductValidator()));
```

## Failure Behavior

`ValidationOptions.FailureBehavior` controls what happens when at least one
entity is rejected:

| Value | Behavior |
|---|---|
| `RecordAsFailure` (default) | Each invalid entity becomes a failure with `FailureReason.ValidationError`. Valid entities still hit the database. Matches the "winnow out the failures" model the rest of the library follows. |
| `Throw` | A `WinnowValidationException` is thrown after the entire batch is scanned. The exception carries every failure reported. No database round trips occur. Use this when validation failures indicate a bug rather than a data-quality issue you want to capture per-entity. The scan is not short-circuited on the first failure — every offending entity is included so callers can react to them all. |

Pass the behavior inline so the whole thing is configured in one call:

```csharp
options.WithValidation<Product>(validator, ValidationFailureBehavior.Throw);
// or:
options.WithDataAnnotations<Product>(ValidationFailureBehavior.Throw);
```

The two-step form remains valid for code that needs to flip the behavior after construction:

```csharp
options.WithValidation<Product>(validator);
options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;
```

## Cancellation

Validation honours the `CancellationToken` passed to the `*Async` methods. The pipeline polls the token every `ValidationOptions.CancellationCheckInterval` entities (default: 256). Lower this for faster cancellation response at a small throughput cost; raise it if your validator is so cheap that the poll dominates.

```csharp
options.Validation!.CancellationCheckInterval = 32;
```

## Graph Operations

By default, pre-validation walks only the top-level entities passed to `InsertGraph`, `UpdateGraph`, `DeleteGraph`, or `UpsertGraph` — navigation children are not validated. `IncludeNavigations` lives on `GraphValidationOptions` (a subtype of `ValidationOptions`) and is only accessible when validation is attached to a graph options object — the type system makes it impossible to set on a flat `InsertOptions`/`DeleteOptions`/`UpsertOptions`. Set `IncludeNavigations = true` to opt into walking the entity's reference and collection navigations and applying DataAnnotations to each reachable child:

```csharp
var options = new InsertGraphOptions()
    .WithDataAnnotations<Order>(includeNavigations: true);
```

Or set it after the fact if you prefer:

```csharp
var options = new InsertGraphOptions().WithDataAnnotations<Order>();
options.Validation!.IncludeNavigations = true;   // GraphValidationOptions
```

Child failures are reported on the parent's failure record with a property path that locates the offending value, for example `"Items[2].Sku"`. Cycle protection is reference-based: if a child links back to an already-visited parent it is skipped, so self-referencing graphs terminate cleanly. The walk also honours `GraphOptionsBase.NavigationFilter` — navigations excluded by the filter are not validated, matching the scope of the graph operation that owns the walk. Validation also recurses through unannotated intermediate types when a deeper child has DataAnnotations, so a `Root → Mid (no annotations) → Leaf [Required]` graph still surfaces leaf failures.

The walker stops at `GraphValidationOptions.MaxNavigationDepth` (default 32) and records a `WINNOW_NAV_DEPTH_LIMIT` validation error at the cut-off point — this is what keeps accidentally-unbounded or deeply-nested graphs from blowing the stack. Raise the cap if your graph is genuinely deep; the cap exists to surface a configuration issue, not to silently drop entities.

> `IncludeNavigations` requires a DataAnnotations-built validator
> (`WithDataAnnotations<TEntity>()`). A typed `ValidatorDelegate<TEntity>`
> can only run against `TEntity` and cannot validate children of differing
> types — assigning `IncludeNavigations = true` to a `GraphValidationOptions`
> built with a custom delegate throws `InvalidOperationException` at the
> point of assignment, not when the first batch runs. Pair custom delegates
> with a separate options instance per child type if you need polymorphic
> validation.

## Result Shape

Pre-validation failures appear in `result.Failures` exactly like any other failure, with `Reason = FailureReason.ValidationError`:

- **Insert / InsertGraph**: failures are keyed by `EntityIndex` (the position in the original input list).
- **Update / Delete / UpdateGraph / DeleteGraph**: failures are keyed by `EntityId` (read via reflection from the entity's primary-key property).
- **Upsert / UpsertGraph**: failures carry both `EntityIndex` and `EntityId`. `AttemptedOperation` is set heuristically by checking `HasDefaultKeyValue(entity)` — default key → `Insert`, otherwise `Update`.

Each failure also exposes the structured `ValidationErrors` list — the same `ValidationError` instances the validator emitted. Drive UI / API responses off this list rather than parsing `ErrorMessage`:

```csharp
foreach (var failure in result.Failures.Where(f => f.Reason == FailureReason.ValidationError))
{
    foreach (var error in failure.ValidationErrors!)
    {
        _logger.LogWarning("Entity {Index} property {Property}: {Code} {Message}",
            failure.EntityIndex, error.PropertyName, error.Code, error.Message);
    }
}
```

`ValidationErrors` is `null` on every non-validation failure, so checking `Reason == FailureReason.ValidationError` (or just `ValidationErrors is not null`) is enough to gate the structured access.

`SuccessCount`, `FailureCount`, and `DatabaseRoundTrips` remain accurate at every `ResultDetail` level.

## ParallelWinnower

`ParallelWinnower<TEntity, TKey>` partitions the input list across multiple `DbContext` instances. Pre-validation runs inside each partition's strategy — there is no shared validator state, so a `ValidatorDelegate<TEntity>` (or `WithDataAnnotations`) can rely on the same single-threaded guarantees the synchronous path provides. The reflection cache used by `WithDataAnnotations` is thread-safe (`ConcurrentDictionary`) so configuring the same options across partitions has no observable effect on correctness or throughput.

`EntityIndex` on failures is reported relative to the **partition's** input, not the caller's global list. Use `ParallelWinnower`'s aggregated result accessors if you need global indexing; pre-validation does not change that contract.

## Performance Notes

- **Hot path**: one delegate cast per batch, then a tight `for` loop over the entity list. No LINQ, no enumerator allocation.
- **Allocations**: one 4-slot `ValidationError[]` buffer allocated per batch and reused across every entity. The collector only rents from `ArrayPool<T>.Shared` when a single entity emits more errors than the inline capacity (rare). The buffer is heap-allocated rather than stack-allocated because `ValidationError` contains string references and is not an `unmanaged` type — the per-batch amortisation keeps the cost well below one allocation per entity.
- **DataAnnotations adapter**: reflection cost is paid once per type via a `ConcurrentDictionary` cache; each annotated property's getter is compiled to a direct expression delegate so the per-entity hot path is a linear walk over cached `(compiled getter, ValidationAttribute[])` pairs with no `PropertyInfo.GetValue` reflection per entity.
- **Cancellation poll**: one volatile read every 256 entities by default; branch-predictable and rounded to a power of two for the modulo to optimise to a bitmask under JIT inlining.

See `benchmarks/Winnow.Benchmarks/Benchmarks/PreValidationBenchmarks.cs` for the numbers backing these claims; the entity shape and failure generator match `FailureRateBenchmarks` so results compare directly against the README's failure-rate table.
