# Composite Keys

BatchSaver fully supports entities with composite primary keys.

## API Decision Tree

```
Is your entity's primary key composite (multiple columns)?
├── No  → Use BatchSaver<TEntity, TKey> with the key type (int, long, Guid, string)
└── Yes → Choose one:
          ├── Auto-detect: BatchSaver<TEntity>(context) - returns CompositeKey
          └── Explicit:    BatchSaver<TEntity, CompositeKey>(context)
```

## CompositeKey Struct

The `CompositeKey` struct represents multi-part primary keys with proper equality semantics:

```csharp
// Two-part key (e.g., OrderId + LineNumber)
var key = new CompositeKey(orderId, lineNumber);

// Three-part key (e.g., WarehouseCode + AisleNumber + BinCode)
var key = new CompositeKey("WH01", 5, "BIN-A");
```

## Value Extraction

```csharp
var key = new CompositeKey("WH01", 5, "BIN-A");

// By index
key.GetValue<string>(0)  // "WH01"
key.GetValue<int>(1)     // 5
key.GetValue<string>(2)  // "BIN-A"

// For single-component keys (auto-detect mode with simple key)
if (key.IsSingle)
{
    int id = key.AsSingle<int>();
}

// Deconstruction (2, 3, or 4 parts)
var (orderId, lineNumber) = key;              // 2-part
var (warehouse, aisle, bin) = threePartKey;   // 3-part
```

## Auto-Detect API

When you don't specify a key type, BatchSaver auto-detects composite keys:

```csharp
// Simple key entity - works automatically
var productSaver = new BatchSaver<Product>(context);
productSaver.IsCompositeKey.ShouldBeFalse();

// Composite key entity - detected automatically
var orderLineSaver = new BatchSaver<OrderLine>(context);
orderLineSaver.IsCompositeKey.ShouldBeTrue();

// Insert and extract IDs
var result = orderLineSaver.InsertBatch([orderLine]);
var insertedKey = result.InsertedIds[0];

// Access components
int orderId = insertedKey.GetValue<int>(0);
int lineNumber = insertedKey.GetValue<int>(1);
```

## Explicit CompositeKey Usage

For explicit typing, specify `CompositeKey` as the key type:

```csharp
var saver = new BatchSaver<OrderLine, CompositeKey>(context);

var orderLines = new[]
{
    new OrderLine { OrderId = 1, LineNumber = 1, Quantity = 5 },
    new OrderLine { OrderId = 1, LineNumber = 2, Quantity = 3 }
};

var result = saver.InsertBatch(orderLines);

// Results use CompositeKey
foreach (var key in result.InsertedIds)
{
    Console.WriteLine($"Order {key.GetValue<int>(0)}, Line {key.GetValue<int>(1)}");
}
```

## Three-Part Key Example

```csharp
// Entity with string + int + string composite key
public class InventoryLocation
{
    public string WarehouseCode { get; set; }
    public int AisleNumber { get; set; }
    public string BinCode { get; set; }
    public int Quantity { get; set; }
}

var saver = new BatchSaver<InventoryLocation, CompositeKey>(context);
var result = saver.InsertBatch(locations);

var firstKey = result.InsertedIds[0];
var (warehouse, aisle, bin) = firstKey;  // Deconstruct

// Or access by index with type conversion
string warehouseCode = firstKey.GetValue<string>(0);  // "WH01"
int aisleNumber = firstKey.GetValue<int>(1);          // 5
string binCode = firstKey.GetValue<string>(2);        // "BIN-A"
```

## Graph Operations with Composite Keys

All graph operations work with composite key parents:

```csharp
var saver = new BatchSaver<OrderLine, CompositeKey>(context);

// OrderLine (composite key) → OrderLineNote (simple key)
var orderLine = new OrderLine
{
    OrderId = orderId,
    LineNumber = 1,
    Quantity = 5,
    Notes = [
        new OrderLineNote { Note = "Note 1" },
        new OrderLineNote { Note = "Note 2" }
    ]
};

var result = saver.InsertGraphBatch([orderLine]);

// Access hierarchy with composite parent key
var parentKey = new CompositeKey(orderId, 1);
var childIds = result.ChildIdsByParentId![parentKey];
```
