using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverUpsertGraphTests : TestBase
{
    [Fact]
    public void UpsertGraphBatch_NewParentNewChildren_AllInserted()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 3).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-NEW-{i:D3}",
            CustomerName = $"New Customer {i}",
            CustomerId = 1000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 2).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Product {j}",
                Quantity = j + 1,
                UnitPrice = 25.00m,
                Subtotal = (j + 1) * 25.00m
            }).ToList()
        }).ToList();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(3);
        result.UpdatedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(3);
        context.OrderItems.Count().ShouldBe(6);
    }

    [Fact]
    public void UpsertGraphBatch_ExistingParentNewChildren_Mixed()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        orders[0].Status = CustomerOrderStatus.Processing;
        orders[0].OrderItems.Add(new OrderItem
        {
            ProductId = 9001,
            ProductName = "New Child Product",
            Quantity = 3,
            UnitPrice = 15.00m,
            Subtotal = 45.00m
        });

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        var updatedOrder = context.CustomerOrders.Include(o => o.OrderItems)
            .First(o => o.Id == orders[0].Id);
        updatedOrder.OrderItems.Count.ShouldBe(3);
    }

    [Fact]
    public void UpsertGraphBatch_NewParentExistingChildren_Mixed()
    {
        using var context = CreateContext();

        // First create some order items that we'll reference
        var existingOrder = new CustomerOrder
        {
            OrderNumber = "ORD-EXISTING",
            CustomerName = "Existing Customer",
            CustomerId = 500,
            Status = CustomerOrderStatus.Completed,
            TotalAmount = 50.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 5001,
                    ProductName = "Existing Product",
                    Quantity = 2,
                    UnitPrice = 25.00m,
                    Subtotal = 50.00m
                }
            ]
        };
        context.CustomerOrders.Add(existingOrder);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Create a new parent order
        var newOrder = new CustomerOrder
        {
            OrderNumber = "ORD-NEWPARENT",
            CustomerName = "New Parent Customer",
            CustomerId = 600,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 75.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 6001,
                    ProductName = "Fresh Product",
                    Quantity = 3,
                    UnitPrice = 25.00m,
                    Subtotal = 75.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([newOrder]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_ExistingParentExistingChildren_AllUpdated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Completed;
            foreach (var item in order.OrderItems)
            {
                item.Quantity += 1;
                item.Subtotal = item.Quantity * item.UnitPrice;
            }
        }

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(3);
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void UpsertGraphBatch_MixedAtAllLevels()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 2);

        var existingOrders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        existingOrders[0].Status = CustomerOrderStatus.Processing;

        var newOrder = new CustomerOrder
        {
            OrderNumber = "ORD-MIXLEVEL",
            CustomerName = "Mixed Level Customer",
            CustomerId = 9999,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 7001,
                    ProductName = "Mixed Product",
                    Quantity = 4,
                    UnitPrice = 50.00m,
                    Subtotal = 200.00m
                }
            ]
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(existingOrders.Append(newOrder));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_ThreeLevel_Works()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-3LEVEL",
            CustomerName = "Three Level Customer",
            CustomerId = 1234,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 300.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 8001,
                    ProductName = "3-Level Product",
                    Quantity = 5,
                    UnitPrice = 60.00m,
                    Subtotal = 300.00m,
                    Reservations =
                    [
                        new ItemReservation
                        {
                            WarehouseLocation = "WH-A1",
                            ReservedQuantity = 3,
                            ReservedAt = DateTimeOffset.UtcNow
                        },
                        new ItemReservation
                        {
                            WarehouseLocation = "WH-B2",
                            ReservedQuantity = 2,
                            ReservedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(1);
        context.ItemReservations.Count().ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_OrphanThrow_ThrowsOnRemoval()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpsertGraphBatch(orders));

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedItemId.ToString());
    }

    [Fact]
    public void UpsertGraphBatch_OrphanDelete_DeletesOrphans()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems)
            .First(o => o.Id == orders[0].Id);
        verifyOrder.OrderItems.Count.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_OrphanDetach_DetachesOrphans()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 2, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var orphanedItem = context.OrderItems.Find(removedItemId);
        orphanedItem.ShouldNotBeNull();
    }

    [Fact]
    public void UpsertGraphBatch_GraphHierarchy_Populated()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 2).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-HIER-{i:D3}",
            CustomerName = $"Hierarchy Customer {i}",
            CustomerId = 2000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 150.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 3).Select(j => new OrderItem
            {
                ProductId = 3000 + j,
                ProductName = $"Hierarchy Product {j}",
                Quantity = j,
                UnitPrice = 50.00m,
                Subtotal = j * 50.00m
            }).ToList()
        }).ToList();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(2);

        foreach (var order in orders)
        {
            result.GraphHierarchy!.ShouldContain(n => n.EntityId.Equals(order.Id));
            var node = result.GraphHierarchy!.First(n => n.EntityId.Equals(order.Id));
            node.GetChildIds().Count.ShouldBe(3);
        }
    }

    [Fact]
    public void UpsertGraphBatch_TraversalInfo_Accurate()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-TRAVERSE",
            CustomerName = "Traversal Customer",
            CustomerId = 4000,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 4).Select(j => new OrderItem
            {
                ProductId = 4000 + j,
                ProductName = $"Traverse Product {j}",
                Quantity = j,
                UnitPrice = 50.00m,
                Subtotal = j * 50.00m
            }).ToList()
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.TotalEntitiesTraversed.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpsertGraphBatch_WithReferences_Works()
    {
        using var context = CreateContext();

        // Create a category first
        var category = new Category
        {
            Name = "Electronics",
            Description = "Electronic products"
        };
        context.Categories.Add(category);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Create a product with the category reference (FK only, no navigation)
        var product = new Product
        {
            Name = "Reference Product",
            Price = 100.00m,
            Stock = 50,
            LastModified = DateTimeOffset.UtcNow,
            CategoryId = category.Id
            // Category navigation is NOT populated - just the FK
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedProduct = context.Products.Include(p => p.Category).First();
        savedProduct.CategoryId.ShouldBe(category.Id);
        savedProduct.Category.ShouldNotBeNull();
    }

    [Fact]
    public void UpsertGraphBatch_ChildFailure_FailsGraph()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = -1; // Invalid child

        orders[1].Status = CustomerOrderStatus.Processing;
        orders[2].Status = CustomerOrderStatus.Cancelled;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
    }

    // === M2M TESTS: Skip Navigation (Student ↔ Courses) ===

    [Fact]
    public void UpsertGraphBatch_SkipNavigation_NewStudentWithNewCourses_JoinRecordsCreated()
    {
        using var context = CreateContext();
        SeedCourses(context, 3);

        var courses = context.Courses.ToList();
        var student = CreateStudent("Alice", [courses[0], courses[1]]);

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([student], new UpsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedStudent = context.Students.Include(s => s.Courses).First(s => s.Id == student.Id);
        savedStudent.Courses.Count.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_SkipNavigation_NewStudentWithExistingCourses_JoinRecordsCreated()
    {
        using var context = CreateContext();
        SeedCourses(context, 4);

        // Get courses and create a new student with references to existing courses
        var courses = context.Courses.Take(2).ToList();
        var student = CreateStudent("Bob", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([student], new UpsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedStudent = context.Students.Include(s => s.Courses).First(s => s.Id == student.Id);
        savedStudent.Courses.Count.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphBatch_SkipNavigation_MultipleNewStudents_AllJoinRecordsCreated()
    {
        using var context = CreateContext();
        SeedCourses(context, 4);

        var courses = context.Courses.ToList();
        var student1 = CreateStudent("Carol", [courses[0], courses[1]]);
        var student2 = CreateStudent("Dave", [courses[2], courses[3]]);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([student1, student2], new UpsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        var savedStudents = context.Students.Include(s => s.Courses).ToList();
        savedStudents.Sum(s => s.Courses.Count).ShouldBe(4);
    }

    [Fact]
    public void UpsertGraphBatch_SkipNavigation_MixedNewAndExistingStudents_BothWork()
    {
        using var context = CreateContext();
        SeedCourses(context, 4);

        var courses = context.Courses.ToList();

        // Create existing student first
        var existingStudent = CreateStudent("Eve");
        context.Students.Add(existingStudent);
        context.SaveChanges();
        var existingStudentId = existingStudent.Id;
        context.ChangeTracker.Clear();

        // Prepare new student with courses
        var newStudent = CreateStudent("Frank", [courses[0], courses[1]]);

        // Load existing student and update name
        var loadedExisting = context.Students.First(s => s.Id == existingStudentId);
        loadedExisting.Name = "Eve Updated";

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([loadedExisting, newStudent], new UpsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedExisting = context.Students.First(s => s.Id == existingStudentId);
        var savedNew = context.Students.Include(s => s.Courses).First(s => s.Id == newStudent.Id);

        savedExisting.Name.ShouldBe("Eve Updated");
        savedNew.Courses.Count.ShouldBe(2);
    }

    // === M2M TESTS: Explicit Join (Student ↔ Enrollment ↔ Course) ===

    [Fact]
    public void UpsertGraphBatch_ExplicitJoin_NewStudentWithEnrollments_Works()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courses = context.Courses.ToList();

        var student = new Student
        {
            Name = "Frank",
            Email = "frank@example.com",
            Enrollments =
            [
                CreateEnrollment(null!, courses[0], "A"),
                CreateEnrollment(null!, courses[1])
            ]
        };

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([student]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedStudent = context.Students.Include(s => s.Enrollments).First(s => s.Id == student.Id);
        savedStudent.Enrollments.Count.ShouldBe(2);
        savedStudent.Enrollments.First(e => e.CourseId == courses[0].Id).Grade.ShouldBe("A");
    }

    [Fact]
    public void UpsertGraphBatch_ExplicitJoin_ExistingStudentModifyEnrollment_PayloadPreserved()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courses = context.Courses.ToList();

        var student = new Student
        {
            Name = "Grace",
            Email = "grace@example.com",
            Enrollments = [CreateEnrollment(null!, courses[0])]
        };
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var existingStudent = context.Students.Include(s => s.Enrollments).First(s => s.Id == student.Id);
        existingStudent.Enrollments.First().Grade = "B+";

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([existingStudent]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var savedStudent = context.Students.Include(s => s.Enrollments).First(s => s.Id == student.Id);
        savedStudent.Enrollments.First().Grade.ShouldBe("B+");
    }

    [Fact]
    public void UpsertGraphBatch_ExplicitJoin_RemoveEnrollment_WithOrphanDelete()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courses = context.Courses.ToList();

        var student = new Student
        {
            Name = "Henry",
            Email = "henry@example.com",
            Enrollments =
            [
                CreateEnrollment(null!, courses[0]),
                CreateEnrollment(null!, courses[1])
            ]
        };
        context.Students.Add(student);
        context.SaveChanges();
        var studentId = student.Id;
        var enrollmentToRemoveId = student.Enrollments.First().Id;
        context.ChangeTracker.Clear();

        var existingStudent = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        existingStudent.Enrollments.Remove(existingStudent.Enrollments.First());

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([existingStudent], new UpsertGraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Enrollments.Find(enrollmentToRemoveId).ShouldBeNull();
        var savedStudent = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        savedStudent.Enrollments.Count.ShouldBe(1);
    }

    [Fact]
    public void UpsertGraphBatch_M2M_TraversalInfo_TracksJoinRecords()
    {
        using var context = CreateContext();
        SeedCourses(context, 3);
        var courses = context.Courses.ToList();

        var student = CreateStudent("Ivy", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpsertGraphBatch([student], new UpsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBeGreaterThanOrEqualTo(3);
    }

    // === REFERENCE NAVIGATION TESTS ===

    [Fact]
    public void UpsertGraphBatch_WithReference_NewOrderNewProduct_BothInserted()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "New Product",
            Price = 50.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-001",
            CustomerName = "Reference Customer",
            CustomerId = 5000,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m,
                    Product = product
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order], new UpsertGraphBatchOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(1);
    }

    [Fact]
    public void UpsertGraphBatch_WithReference_ExistingOrderUpdateItem_Updated()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-002",
            CustomerName = "Update Customer",
            CustomerId = 5001,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 60.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Product 1",
                    Quantity = 2,
                    UnitPrice = 30.00m,
                    Subtotal = 60.00m
                }
            ]
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();

        var existingOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.Id == orderId);

        existingOrder.Status = CustomerOrderStatus.Processing;
        existingOrder.OrderItems.First().Quantity = 5;
        existingOrder.OrderItems.First().Subtotal = 150.00m;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([existingOrder]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var updatedOrder = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orderId);
        updatedOrder.Status.ShouldBe(CustomerOrderStatus.Processing);
        updatedOrder.OrderItems.First().Quantity.ShouldBe(5);
    }

    [Fact]
    public void UpsertGraphBatch_WithReference_NewOrderExistingProduct_OrderInsertedProductUnchanged()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Existing Reference Product",
            Price = 40.00m,
            Stock = 75,
            LastModified = DateTimeOffset.UtcNow
        };
        context.Products.Add(product);
        context.SaveChanges();
        var originalPrice = product.Price;
        context.ChangeTracker.Clear();

        var existingProduct = context.Products.First();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-003",
            CustomerName = "New Order Customer",
            CustomerId = 5002,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 80.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = existingProduct.Id,
                    ProductName = existingProduct.Name,
                    Quantity = 2,
                    UnitPrice = 40.00m,
                    Subtotal = 80.00m,
                    Product = existingProduct
                }
            ]
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order], new UpsertGraphBatchOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var savedProduct = context.Products.Find(product.Id);
        savedProduct!.Price.ShouldBe(originalPrice);
    }

    [Fact]
    public void UpsertGraphBatch_MultiLevelReference_CategoryProductOrder_AllUpserted()
    {
        using var context = CreateContext();

        var category = new Category
        {
            Name = "Multi-Level Category",
            Description = "For multi-level test"
        };
        context.Categories.Add(category);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var product = new Product
        {
            Name = "Multi-Level Product",
            Price = 45.00m,
            Stock = 60,
            LastModified = DateTimeOffset.UtcNow,
            CategoryId = category.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-004",
            CustomerName = "Multi-Level Customer",
            CustomerId = 5003,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 90.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = 2,
                    UnitPrice = 45.00m,
                    Subtotal = 90.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order], new UpsertGraphBatchOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            MaxDepth = 5
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
        context.OrderItems.Count().ShouldBe(1);
    }

    [Fact]
    public void UpsertGraphBatch_CircularReference_IgnoreMode_Works()
    {
        using var context = CreateContext();

        var parentCategory = new Category
        {
            Name = "Parent Category",
            Description = "Parent"
        };
        context.Categories.Add(parentCategory);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var childCategory = new Category
        {
            Name = "Child Category",
            Description = "Child",
            ParentCategoryId = parentCategory.Id
        };

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.UpsertGraphBatch([childCategory], new UpsertGraphBatchOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(2);
    }

    // === HELPER METHODS ===

    private static Student CreateStudent(string name, ICollection<Course>? courses = null) => new()
    {
        Name = name,
        Email = $"{name.ToLower()}@example.com",
        Courses = courses ?? []
    };

    private static Course CreateCourse(string code, int credits = 3) => new()
    {
        Code = code,
        Title = $"Course {code}",
        Credits = credits
    };

    private static Enrollment CreateEnrollment(Student student, Course course, string? grade = null) => new()
    {
        Student = student,
        Course = course,
        CourseId = course.Id,
        EnrolledAt = DateTime.UtcNow,
        Grade = grade
    };

    private void SeedCourses(TestDbContext context, int count = 5)
    {
        var courses = Enumerable.Range(1, count).Select(i => CreateCourse($"CS{i:D3}")).ToList();
        context.Courses.AddRange(courses);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
