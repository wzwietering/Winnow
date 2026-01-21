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
