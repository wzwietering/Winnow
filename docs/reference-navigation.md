# Reference Navigation (Many-to-One)

Graph operations can include reference navigations (many-to-one relationships) in addition to collections.

## Basic Usage

```csharp
var saver = new BatchSaver<OrderItem, int>(context);

var items = context.OrderItems
    .Include(i => i.Product)  // Include the reference
    .ToList();

// Update both OrderItem and its referenced Product
items[0].Quantity = 5;
items[0].Product.Price = 29.99m;

var result = saver.UpdateGraphBatch(items, new GraphBatchOptions
{
    IncludeReferences = true,
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```

## Reference Options

| Option | Default | Description |
|--------|---------|-------------|
| `IncludeReferences` | `false` | Include many-to-one references in traversal |
| `CircularReferenceHandling` | `Throw` | `Throw` or `Ignore` circular references |
| `MaxDepth` | `10` | Maximum traversal depth (applies to both collections and references) |

## Circular Reference Handling

When entities reference each other (e.g., `Order` → `Customer` → `Orders`), you need to handle circular references:

```csharp
var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    IncludeReferences = true,
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```

| Mode | Behavior |
|------|----------|
| `Throw` (default) | Exception on circular reference - prevents infinite loops |
| `Ignore` | Process each entity once, skip revisits |

## Tracking Processed References

After a graph operation, you can see which references were processed:

```csharp
var traversalInfo = result.TraversalInfo;

// Count of unique references processed
Console.WriteLine($"References processed: {traversalInfo?.UniqueReferencesProcessed}");

// Grouped by type
foreach (var (typeName, ids) in traversalInfo?.ProcessedReferencesByType ?? [])
{
    Console.WriteLine($"{typeName}: {string.Join(", ", ids)}");
}
```
