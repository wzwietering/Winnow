# EfCoreUtils

Efficient batch utilities for Entity Framework Core with failure isolation and tracking.

## Features

- **Batch Inserts**: Insert many entities with failure isolation and ID tracking
- **Batch Updates**: Update many entities while tracking success/failure per entity
- **Batch Deletes**: Delete many entities with cascade behavior options
- **Graph Operations**: Handle parent + children together (insert, update, delete)
- **Reference Navigation**: Include many-to-one references in graph operations
- **Many-to-Many Relationships**: Support for skip navigations and explicit join entities with join record tracking
- **Two Strategies**: OneByOne (predictable) vs DivideAndConquer (optimized for low failure rates)
- **Detailed Results**: Track successful IDs, failures with reasons, round trips, and duration

## Quick Start

```csharp
// Specify entity type and key type
var saver = new BatchSaver<Product, int>(context);

// Insert
var insertResult = saver.InsertBatch(newProducts);
Console.WriteLine($"Inserted IDs: {string.Join(", ", insertResult.InsertedIds)}");

// Update
var updateResult = saver.UpdateBatch(existingProducts);

// Delete
var deleteResult = saver.DeleteBatch(productsToRemove);

// Check results
Console.WriteLine($"Saved: {updateResult.SuccessCount}, Failed: {updateResult.FailureCount}");
foreach (var failure in updateResult.Failures)
{
    Console.WriteLine($"  {failure.EntityId}: {failure.ErrorMessage}");
}
```

## Supported Key Types

BatchSaver supports any key type that implements `IEquatable<TKey>`:

```csharp
// Integer keys (most common)
var saver = new BatchSaver<Product, int>(context);

// Long keys
var saver = new BatchSaver<Order, long>(context);

// GUID keys
var saver = new BatchSaver<Document, Guid>(context);

// String keys
var saver = new BatchSaver<Setting, string>(context);
```

If you specify the wrong key type, you'll get a descriptive error:
```
Primary key type mismatch for entity Product. Expected type Int64, but entity has key type Int32.
Use BatchSaver<Product, Int32> instead.
```

## Composite Keys

BatchSaver fully supports entities with composite primary keys.

### API Decision Tree

```
Is your entity's primary key composite (multiple columns)?
├── No  → Use BatchSaver<TEntity, TKey> with the key type (int, long, Guid, string)
└── Yes → Choose one:
          ├── Auto-detect: BatchSaver<TEntity>(context) - returns CompositeKey
          └── Explicit:    BatchSaver<TEntity, CompositeKey>(context)
```

### CompositeKey Struct

The `CompositeKey` struct represents multi-part primary keys with proper equality semantics:

```csharp
// Two-part key (e.g., OrderId + LineNumber)
var key = new CompositeKey(orderId, lineNumber);

// Three-part key (e.g., WarehouseCode + AisleNumber + BinCode)
var key = new CompositeKey("WH01", 5, "BIN-A");
```

### Value Extraction

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

### Auto-Detect API

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

### Explicit CompositeKey Usage

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

### Three-Part Key Example

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

### Graph Operations with Composite Keys

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

## Insert Operations

### Basic Insert

```csharp
var saver = new BatchSaver<Product, int>(context);

var products = new List<Product>
{
    new Product { Name = "Widget", Price = 9.99m },
    new Product { Name = "Gadget", Price = 19.99m }
};

var result = saver.InsertBatch(products, new InsertBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Access generated IDs
foreach (var inserted in result.InsertedEntities)
{
    Console.WriteLine($"Index {inserted.OriginalIndex} got ID {inserted.Id}");
}
```

### Graph Insert (Parent + Children)

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = new List<CustomerOrder>
{
    new CustomerOrder
    {
        CustomerName = "Alice",
        OrderItems = new List<OrderItem>
        {
            new OrderItem { ProductId = 1, Quantity = 2 },
            new OrderItem { ProductId = 2, Quantity = 1 }
        }
    }
};

var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});

// Parent and child IDs are populated after insert
foreach (var parentId in result.InsertedIds)
{
    var childIds = result.ChildIdsByParentId![parentId];
    Console.WriteLine($"Order {parentId} has items: {string.Join(", ", childIds)}");
}
```

## Delete Operations

### Basic Delete

```csharp
var saver = new BatchSaver<Product, int>(context);

var result = saver.DeleteBatch(productsToRemove, new DeleteBatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});
```

### Graph Delete (Parent + Children)

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = context.CustomerOrders
    .Include(o => o.OrderItems)
    .Where(o => o.Status == OrderStatus.Cancelled)
    .ToList();

var result = saver.DeleteGraphBatch(orders, new DeleteGraphBatchOptions
{
    Strategy = BatchStrategy.OneByOne,
    CascadeBehavior = DeleteCascadeBehavior.Cascade
});
```

### Cascade Behavior

When deleting parent entities with children:

| Behavior | Effect |
|----------|--------|
| `Cascade` (default) | Delete children first, then parent. Always works. |
| `Throw` | Exception if parent has loaded children. |
| `ParentOnly` | Only delete parent, rely on database CASCADE DELETE. |

## Upsert Operations

Upsert operations perform INSERT or UPDATE based on key detection:
- **Default key** (0, Guid.Empty, null) → INSERT
- **Non-default key** → UPDATE

> **Warning**: This is NOT a database-level MERGE. There is a potential race condition between key detection and SaveChanges. If another process inserts a row between these operations, you may get conflicts. Use retry logic or database-level upsert for high-concurrency scenarios.

### Basic Upsert

```csharp
var saver = new BatchSaver<Product, int>(context);

var products = new[]
{
    new Product { Id = 0, Name = "New Product", Price = 9.99m },     // INSERT (Id=0)
    new Product { Id = 42, Name = "Updated Name", Price = 19.99m }, // UPDATE (Id=42)
    new Product { Id = 0, Name = "Another New", Price = 5.00m }     // INSERT (Id=0)
};

var result = saver.UpsertBatch(products);

Console.WriteLine($"Inserted: {result.InsertedCount}");  // 2
Console.WriteLine($"Updated: {result.UpdatedCount}");    // 1

// Access results by operation type
foreach (var entity in result.InsertedEntities)
{
    Console.WriteLine($"Inserted product: {entity.Id}");
}
foreach (var entity in result.UpdatedEntities)
{
    Console.WriteLine($"Updated product: {entity.Id}");
}
```

### Graph Upsert (Parent + Children)

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = new[]
{
    new CustomerOrder
    {
        Id = 0,  // New order (insert)
        CustomerName = "Alice",
        OrderItems = new List<OrderItem>
        {
            new() { Id = 0, ProductName = "Widget", Quantity = 5 },   // INSERT
            new() { Id = 123, ProductName = "Gadget", Quantity = 3 }  // UPDATE
        }
    }
};

var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete  // Required for graph updates
});

Console.WriteLine($"Inserted: {result.InsertedCount}");
Console.WriteLine($"Updated: {result.UpdatedCount}");
```

### UpsertBatchResult Properties

```csharp
result.InsertedEntities   // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.UpdatedEntities    // List<UpsertedEntity<TKey>> with Id, OriginalIndex, Entity, Operation
result.InsertedIds        // IReadOnlyList<TKey> - Just the inserted IDs
result.UpdatedIds         // IReadOnlyList<TKey> - Just the updated IDs
result.SuccessfulIds      // IReadOnlyList<TKey> - All successful IDs (inserted + updated)
result.InsertedCount      // Count of inserts
result.UpdatedCount       // Count of updates
result.Failures           // List<UpsertBatchFailure<TKey>> with EntityIndex, AttemptedOperation
result.GraphHierarchy     // For graph upserts: parent ID -> GraphNode
result.TraversalInfo      // Graph traversal statistics
```

## Update Operations

### Basic Update

```csharp
var saver = new BatchSaver<Product, int>(context);

var result = saver.UpdateBatch(products);
```

### Graph Update (Parent + Children)

```csharp
var saver = new BatchSaver<CustomerOrder, int>(context);

var orders = context.CustomerOrders
    .Include(o => o.OrderItems)
    .ToList();

// Modify parent and children
orders[0].Status = OrderStatus.Shipped;
orders[0].OrderItems[0].Quantity = 10;
orders[0].OrderItems.Add(new OrderItem { ... });
orders[0].OrderItems.Remove(orders[0].OrderItems.Last());

var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
{
    OrphanedChildBehavior = OrphanBehavior.Delete
});
```

### Orphan Behavior

When children are removed from a collection during updates:

| Behavior | Effect |
|----------|--------|
| `Throw` (default) | Exception - prevents accidental data loss |
| `Delete` | Removed children are deleted from database |
| `Detach` | Removed children stay in database (may violate FK) |

## Reference Navigation (Many-to-One)

Graph operations can include reference navigations (many-to-one relationships) in addition to collections.

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

### Reference Options

| Option | Default | Description |
|--------|---------|-------------|
| `IncludeReferences` | `false` | Include many-to-one references in traversal |
| `CircularReferenceHandling` | `Throw` | `Throw` or `Ignore` circular references |
| `MaxDepth` | `10` | Maximum traversal depth (applies to both collections and references) |

## Many-to-Many Relationships

Graph operations support many-to-many relationships via skip navigations (EF Core 5+) and explicit join entities.

### Basic Insert with Many-to-Many

```csharp
var saver = new BatchSaver<Student, int>(context);

var student = new Student
{
    Name = "Alice",
    Courses = existingCourses  // Courses already in database
};

var result = saver.InsertGraphBatch(new[] { student }, new InsertGraphBatchOptions
{
    IncludeManyToMany = true  // Enable M2M handling
});

Console.WriteLine($"Students inserted: {result.SuccessCount}");
Console.WriteLine($"Course enrollments created: {result.TraversalInfo?.JoinRecordsCreated}");
```

### Update with Link Changes

```csharp
var student = context.Students
    .Include(s => s.Courses)
    .First(s => s.Id == 1);

// Modify enrollments
student.Courses.Remove(student.Courses.First());  // Drop a course
student.Courses.Add(newCourse);  // Enroll in new course

var result = saver.UpdateGraphBatch(new[] { student }, new GraphBatchOptions
{
    IncludeManyToMany = true
});

// Check what happened
var stats = result.TraversalInfo?.JoinOperationsByNavigation["MyApp.Entities.Student.Courses"];
Console.WriteLine($"Join records added: {stats?.Created}");
Console.WriteLine($"Join records removed: {stats?.Removed}");
```

### Delete with Join Record Cleanup

```csharp
var studentsToDelete = context.Students
    .Include(s => s.Courses)
    .Where(s => s.GraduationYear < 2020)
    .ToList();

var result = saver.DeleteGraphBatch(studentsToDelete, new DeleteGraphBatchOptions
{
    IncludeManyToMany = true  // Clean up join records
});

// Courses remain in database - only join records removed
Console.WriteLine($"Join records deleted: {result.TraversalInfo?.JoinRecordsRemoved}");
```

### Explicit Join Entity with Payload

For join entities with extra properties (e.g., `EnrolledAt`, `Grade`), modify the join entity collection directly:

```csharp
var enrollment = new Enrollment
{
    StudentId = existingStudent.Id,
    CourseId = existingCourse.Id,
    EnrolledAt = DateTime.UtcNow,
    Grade = null
};

student.Enrollments.Add(enrollment);

var result = saver.UpdateGraphBatch(new[] { student }, new GraphBatchOptions
{
    IncludeManyToMany = true
});
```

### Many-to-Many Options

| Option | Default | Description |
|--------|---------|-------------|
| `IncludeManyToMany` | `false` | Include many-to-many navigations in traversal |
| `ManyToManyInsertBehavior` | `AttachExisting` | `AttachExisting` (assume related entities exist) or `InsertIfNew` (insert if default ID) |
| `ValidateManyToManyEntitiesExist` | `true` | Validate related entities exist before creating join records |

## Self-Referencing Hierarchies

Graph operations support self-referencing entities (parent-child hierarchies within the same type).

### Example: Category Hierarchy

```csharp
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = [];
}

// Insert complete hierarchy
var saver = new BatchSaver<Category, int>(context);

var electronics = new Category
{
    Name = "Electronics",
    SubCategories =
    [
        new Category
        {
            Name = "Computers",
            SubCategories =
            [
                new Category { Name = "Laptops" },
                new Category { Name = "Desktops" }
            ]
        },
        new Category { Name = "Phones" }
    ]
};

var result = saver.InsertGraphBatch([electronics]);
// All levels inserted with IDs populated
```

### Circular Reference Handling

For bidirectional navigations (both Parent->Child and Child->Parent loaded):

```csharp
var result = saver.UpdateGraphBatch(categories, new GraphBatchOptions
{
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```

| Mode | Behavior |
|------|----------|
| `Throw` (default) | Exception on circular reference - prevents infinite loops |
| `Ignore` | Process each entity once, skip revisits. Direct self-references still throw. |
| `IgnoreAll` | Allow all patterns including direct self-references (rare use case) |

## Strategies

| Strategy | Best For | Round Trips |
|----------|----------|-------------|
| **OneByOne** | High failure rates, simplicity | N (predictable) |
| **DivideAndConquer** | Low failure rates (<5%) | 1 at 0%, ~150 at 1% |

### Performance (1000 entities, SQLite)

| Failure Rate | OneByOne Trips | D&C Trips | D&C Speedup |
|--------------|----------------|-----------|-------------|
| 0%           | 1000           | 1         | 1.4x faster |
| 1%           | 1000           | 147       | 1.4x faster |
| 5%           | 1000           | 523       | 0.6x (slower) |
| 25%+         | 1000           | 1499+     | Use OneByOne |

**Note**: D&C reduces round trips dramatically but has overhead. With network databases (SQL Server, PostgreSQL), D&C wins more clearly due to latency savings.

## Results

### BatchResult<TKey> (Update/Delete)

```csharp
result.SuccessfulIds      // IReadOnlyList<TKey> - IDs that succeeded
result.Failures           // List<BatchFailure<TKey>> with EntityId, ErrorMessage, Reason
result.ChildIdsByParentId // For graph ops: parent ID -> child IDs (Dictionary<TKey, IReadOnlyList<TKey>>)
result.TraversalInfo      // Graph traversal statistics (see below)
result.DatabaseRoundTrips // Actual DB calls made
result.Duration           // Total time
result.IsCompleteSuccess  // All succeeded
result.IsPartialSuccess   // Some succeeded, some failed
result.IsCompleteFailure  // All failed

// TraversalInfo (for graph operations)
result.TraversalInfo?.ProcessedReferencesByType    // Reference IDs grouped by type name
result.TraversalInfo?.UniqueReferencesProcessed    // Count of unique references processed
result.TraversalInfo?.JoinRecordsCreated           // Count of M2M join records created
result.TraversalInfo?.JoinRecordsRemoved           // Count of M2M join records removed
result.TraversalInfo?.JoinOperationsByNavigation   // Join ops grouped by navigation name
```

### InsertBatchResult<TKey>

```csharp
result.InsertedEntities   // List<InsertedEntity<TKey>> with Id, OriginalIndex, Entity reference
result.InsertedIds        // IReadOnlyList<TKey> - Just the generated IDs
result.Failures           // List<InsertBatchFailure> with EntityIndex, ErrorMessage
result.ChildIdsByParentId // For graph inserts: parent ID -> child IDs (Dictionary<TKey, IReadOnlyList<TKey>>)
result.DatabaseRoundTrips // Actual DB calls made
result.Duration           // Total time
```

### Failure Isolation

Each entity (or graph) succeeds or fails independently:

```
Batch: [Order1 + items, Order2 + items, Order3 + items]
       ✅ saved        ❌ failed         ✅ saved

Order2's failure doesn't affect Order1 or Order3.
```

## When to Use What

| Scenario | Method | Strategy |
|----------|--------|----------|
| Insert new entities | `InsertBatch` | DivideAndConquer (if <5% failures) |
| Insert parent + children | `InsertGraphBatch` | DivideAndConquer |
| Update existing entities | `UpdateBatch` | DivideAndConquer (if <5% failures) |
| Update parent + children | `UpdateGraphBatch` | Set OrphanBehavior explicitly |
| Delete entities | `DeleteBatch` | DivideAndConquer (if <5% failures) |
| Delete parent + children | `DeleteGraphBatch` | Set CascadeBehavior explicitly |
| Include many-to-one refs | `*GraphBatch` | Set `IncludeReferences = true` |
| Include many-to-many | `*GraphBatch` | Set `IncludeManyToMany = true` |
| High failure rate (>25%) | Any | OneByOne |
