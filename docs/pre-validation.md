# Pre-Validation

Winnow's `DivideAndConquer` strategy is fast — until invalid entities show up.
At 0% failures, D&C is 151× faster than `OneByOne` on SQLite. At 25% failures,
it collapses to 1.8× because the recursion has to binary-split every batch that
contains a failure, each split costing one database round trip.

Pre-validation lets you reject invalid entities in-process, **before** the
strategy is invoked. The strategy then only sees valid entities, so its
sub-linear scaling holds across the full failure-rate spectrum.

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

The delegate receives a `ref ValidationCollector` — a stack-only buffer with a
4-slot inline capacity. Emit zero, one, or many `ValidationError` instances per
entity. Zero allocation in the all-valid case; pooled allocation only when one
entity emits more errors than the inline capacity.

## DataAnnotations Adapter

If your entities already carry `[Required]`, `[Range]`, `[StringLength]`, or
similar attributes, Winnow can drive validation from them directly:

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

The first call for a given entity type reflects over its properties to discover
attributes; subsequent calls reuse a `FrozenDictionary` cache, so the reflection
cost is paid once and amortised across all batches.

## FluentValidation Adapter

Winnow does not ship a built-in FluentValidation integration to keep its
dependency surface minimal. Wrap your validator in a delegate:

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

## Failure Behaviour

`ValidationOptions.FailureBehavior` controls what happens when at least one
entity is rejected:

| Value | Behaviour |
|---|---|
| `RecordAsFailure` (default) | Each invalid entity becomes a failure with `FailureReason.ValidationError`. Valid entities still hit the database. Matches the "winnow out the failures" model the rest of the library follows. |
| `Throw` | A `ValidationException` is thrown after the batch is scanned. The exception carries every failure that was reported. No database round trips occur. Use this when validation failures indicate a bug rather than a data-quality issue you want to capture per-entity. |

```csharp
options.WithValidation<Product>(validator);
options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;
```

## Cancellation

Validation honours the `CancellationToken` passed to the `*Async` methods. The
pipeline polls the token every `ValidationOptions.CancellationCheckInterval`
entities (default: 256). Lower this for faster cancellation response at a small
throughput cost; raise it if your validator is so cheap that the poll dominates.

```csharp
options.Validation!.CancellationCheckInterval = 32;
```

## Graph Operations

By default, pre-validation walks only the top-level entities passed to
`InsertGraph`, `UpdateGraph`, `DeleteGraph`, or `UpsertGraph` — navigation
children are not validated. Set `IncludeNavigations = true` to opt into walking
the same navigations the graph save would traverse:

```csharp
var options = new InsertGraphOptions();
options.WithValidation<Order>(validator);
options.Validation!.IncludeNavigations = true;
```

> Note: in the current release, `IncludeNavigations` is reserved for future
> graph descent and behaves the same as `false`. Validate child entities by
> running a separate flat operation, or open an issue if you need graph-aware
> validation sooner.

## Result Shape

Pre-validation failures appear in `result.Failures` exactly like any other
failure, with `Reason = FailureReason.ValidationError`:

- **Insert / InsertGraph**: failures are keyed by `EntityIndex` (the position in
  the original input list).
- **Update / Delete / UpdateGraph / DeleteGraph**: failures are keyed by
  `EntityId` (read via reflection from the entity's primary-key property).
- **Upsert / UpsertGraph**: failures carry both `EntityIndex` and `EntityId`.
  `AttemptedOperation` is set heuristically by checking
  `HasDefaultKeyValue(entity)` — default key → `Insert`, otherwise `Update`.

`SuccessCount`, `FailureCount`, and `DatabaseRoundTrips` remain accurate at every
`ResultDetail` level.

## Performance Notes

- **Hot path**: one delegate cast per batch, then a tight `for` loop over the
  entity list. No LINQ, no enumerator allocation.
- **Allocations**: zero on the all-valid happy path beyond a single
  `ValidationError[4]` buffer reused across the whole batch. Invalid entities
  trigger an `ArrayPool` rental only when one entity emits more errors than the
  inline capacity (rare).
- **DataAnnotations adapter**: reflection cost is paid once per type via
  `ConcurrentDictionary` + `FrozenDictionary`. The per-entity hot path is a
  linear walk over cached `(getter, ValidationAttribute[])` pairs.
- **Cancellation poll**: one volatile read every 256 entities by default;
  branch-predictable and rounded to a power of two for the modulo to optimise to
  a bitmask under JIT inlining.

See `benchmarks/Winnow.Benchmarks/Benchmarks/PreValidationBenchmarks.cs` for the
numbers backing these claims; the entity shape and failure generator match
`FailureRateBenchmarks` so results compare directly against the README's
failure-rate table.
