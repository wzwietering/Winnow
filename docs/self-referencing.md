# Self-Referencing Hierarchies

Graph operations support self-referencing entities (parent-child hierarchies within the same type).

## Example: Category Hierarchy

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
var saver = new Winnower<Category, int>(context);

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

var result = saver.InsertGraph([electronics]);
// All levels inserted with IDs populated
```

## Circular Reference Handling

For bidirectional navigations (both Parent->Child and Child->Parent loaded):

```csharp
var result = saver.UpdateGraph(categories, new GraphOptions
{
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```

| Mode | Behavior |
|------|----------|
| `Throw` (default) | Exception on circular reference - prevents infinite loops |
| `Ignore` | Process each entity once, skip revisits. Direct self-references still throw. |
| `IgnoreAll` | Allow all patterns including direct self-references (rare use case) |

## Depth Limits

For deep hierarchies, you may need to increase the maximum depth:

```csharp
var result = saver.InsertGraph(categories, new InsertGraphOptions
{
    MaxDepth = 20  // Default is 10
});
```

## Navigation Filtering

You can use `NavigationFilter` to control which navigations are traversed for self-referencing entities:

```csharp
var filter = NavigationFilter.Include()
    .Navigation<Category>(c => c.SubCategories);

var result = saver.InsertGraph([parent], new InsertGraphOptions
{
    NavigationFilter = filter,
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```

See [Navigation Filtering](graph-operations.md#navigation-filtering) for details.

## Updating Hierarchies

When updating self-referencing hierarchies, be mindful of orphan behavior:

```csharp
var category = context.Categories
    .Include(c => c.SubCategories)
    .First();

// Move a subcategory to a different parent
var movedCategory = category.SubCategories.First();
category.SubCategories.Remove(movedCategory);
otherCategory.SubCategories.Add(movedCategory);

var result = saver.UpdateGraph([category, otherCategory], new GraphOptions
{
    OrphanedChildBehavior = OrphanBehavior.Detach,  // Don't delete, just reassign
    CircularReferenceHandling = CircularReferenceHandling.Ignore
});
```
