using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Coverage added during the 1.3.0 release readiness pass:
/// ParallelWinnower + validation, IncludeNavigations e2e, Throw on graph/async,
/// mid-validation cancellation for Update/Delete/Upsert, and DataAnnotations
/// cache correctness across multiple types.
/// </summary>
public class ValidationReleaseReadinessTests
{
    // --- Task #7: ParallelWinnower + WithValidation -----------------------

    public class ParallelValidation : ParallelTestBase
    {
        [Fact]
        public async Task InsertAsync_WithValidation_RejectsInvalidAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // every third entity invalid
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
            });

            var result = await saver.InsertAsync(products, options);

            var expectedInvalidIndices = Enumerable.Range(0, 8).Where(i => i % 3 == 0).ToList();
            result.FailureCount.ShouldBe(expectedInvalidIndices.Count);
            result.SuccessCount.ShouldBe(8 - expectedInvalidIndices.Count);
            result.Failures.Select(f => f.EntityIndex).OrderBy(x => x)
                .ShouldBe(expectedInvalidIndices);
            result.Failures.ShouldAllBe(f => f.Reason == FailureReason.ValidationError);
        }

        [Fact]
        public async Task InsertAsync_ValidationFailure_PreservesValidationErrorsAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
            });

            var result = await saver.InsertAsync(products, options);

            result.Failures.ShouldNotBeEmpty();
            foreach (var failure in result.Failures)
            {
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }

        [Fact]
        public async Task UpsertAsync_ValidationFailure_PreservesValidationErrorsAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var factory = CreateContextFactory();
            var saver = new ParallelWinnower<Product, int>(factory, maxDegreeOfParallelism: 2);
            var options = new UpsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
            });

            var result = await saver.UpsertAsync(products, options);

            var validationFailures = result.Failures.Where(f => f.Reason == FailureReason.ValidationError).ToList();
            validationFailures.ShouldNotBeEmpty();
            foreach (var failure in validationFailures)
            {
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }

        // Locks B4: in ParallelWinnower with ThrowAfterBatch, only the entities the
        // validator rejected become failures. Valid siblings in the same partition are
        // NOT swept into the failure bucket — the orchestrator catches the partition's
        // WinnowValidationException, removes the failed entities, and re-runs the
        // partition with the survivors so they reach the database.
        [Fact]
        public async Task InsertAsync_ThrowBehavior_RecordsOnlyOffendingEntitiesAndPersistsSurvivors()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();
            var expectedInvalidIndices = Enumerable.Range(0, 8).Where(i => i % 3 == 0).ToList();
            var expectedValidIndices = Enumerable.Range(0, 8).Where(i => i % 3 != 0).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.ThrowAfterBatch);

            var result = await saver.InsertAsync(products, options);

            result.FailureCount.ShouldBe(expectedInvalidIndices.Count);
            result.SuccessCount.ShouldBe(expectedValidIndices.Count);

            var failureIndices = result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ToList();
            failureIndices.ShouldBe(expectedInvalidIndices);
            foreach (var failure in result.Failures)
            {
                failure.Reason.ShouldBe(FailureReason.ValidationError);
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }
    }

    // --- Task #8: IncludeNavigations end-to-end through InsertGraph -------

    public class IncludeNavigationsEndToEnd : TestBase
    {
        private static CustomerOrder Order(string number, params OrderItem[] items) => new()
        {
            OrderNumber = number,
            CustomerName = "C",
            CustomerId = 1,
            TotalAmount = 1m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = items.ToList(),
        };

        private static OrderItem Item(string? productName) => new()
        {
            ProductId = 1,
            ProductName = productName!,
            Quantity = 1,
            UnitPrice = 1m,
            Subtotal = 1m,
        };

        [Fact]
        public void InsertGraph_DataAnnotationsWithIncludeNavigations_RejectsParentForBadChild()
        {
            using var context = CreateContext();
            var orders = new[]
            {
                Order("OK", Item("Valid")),
                Order("BAD-CHILD", Item(null)),
            };

            var options = new InsertGraphOptions().WithDataAnnotations<CustomerOrder>();
            options.Validation!.IncludeNavigations = true;

            var saver = new Winnower<CustomerOrder, int>(context);
            var result = saver.InsertGraph(orders, options);

            result.SuccessCount.ShouldBe(1);
            result.FailureCount.ShouldBe(1);
            var failure = result.Failures.ShouldHaveSingleItem();
            failure.EntityIndex.ShouldBe(1);
            failure.Reason.ShouldBe(FailureReason.ValidationError);
            failure.ValidationErrors.ShouldNotBeNull();
            failure.ValidationErrors!.ShouldContain(e => e.PropertyName.Contains("ProductName"));

            context.ChangeTracker.Clear();
            context.CustomerOrders.Count(o => o.OrderNumber == "BAD-CHILD").ShouldBe(0);
            context.CustomerOrders.Count(o => o.OrderNumber == "OK").ShouldBe(1);
        }
    }

    // --- Task #9: Throw mode on graph and async ---------------------------

    public class ThrowModeCoverage : TestBase
    {
        private static ValidatorDelegate<Product> AlwaysFail()
            => (Product _, ref ValidationCollector c) => c.Add("X", "always");

        private static ValidatorDelegate<CustomerOrder> RejectOrderNumber(string n)
            => (CustomerOrder o, ref ValidationCollector c) =>
            {
                if (o.OrderNumber == n) c.Add(nameof(CustomerOrder.OrderNumber), "Rejected");
            };

        [Fact]
        public void InsertGraph_ThrowBehavior_ThrowsWinnowValidationException()
        {
            using var context = CreateContext();
            var orders = new[]
            {
                new CustomerOrder
                {
                    OrderNumber = "BAD", CustomerName = "C", CustomerId = 1,
                    TotalAmount = 1m, OrderDate = DateTimeOffset.UtcNow,
                    OrderItems = [new OrderItem { ProductId = 1, ProductName = "P", Quantity = 1, UnitPrice = 1m, Subtotal = 1m }],
                }
            };

            var options = new InsertGraphOptions();
            options.WithValidation(RejectOrderNumber("BAD"));
            options.Validation!.FailureBehavior = ValidationFailureBehavior.ThrowAfterBatch;

            var saver = new Winnower<CustomerOrder, int>(context);
            var ex = Should.Throw<WinnowValidationException>(() => saver.InsertGraph(orders, options));
            ex.Failures.Count.ShouldBe(1);
            ex.Failures[0].EntityIndex.ShouldBe(0);
            ex.Failures[0].Errors.ShouldContain(e => e.PropertyName == nameof(CustomerOrder.OrderNumber));

            context.ChangeTracker.Clear();
            context.CustomerOrders.Count().ShouldBe(0);
        }

        [Fact]
        public async Task InsertAsync_ThrowBehavior_ThrowsWinnowValidationException()
        {
            using var context = CreateContext();
            var products = new[]
            {
                new Product { Name = "A", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            };

            var options = new InsertOptions();
            options.WithValidation(AlwaysFail());
            options.Validation!.FailureBehavior = ValidationFailureBehavior.ThrowAfterBatch;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<WinnowValidationException>(
                () => saver.InsertAsync(products, options));
        }
    }

    // --- Task #10: Mid-validation cancellation for Update/Delete/Upsert ---

    public class MidValidationCancellation : TestBase
    {
        private static List<Product> Pad(int n) =>
            Enumerable.Range(0, n).Select(i => new Product
            {
                Id = i + 100_000,
                Name = $"p{i}",
                Price = 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

        private static WinnowOptions Options(CancellationTokenSource cts, ref int counter)
        {
            // The validator counter must be captured by ref through a wrapper; use a
            // boxed holder so the lambda can mutate it.
            var holder = new Counter { Value = 0 };
            var options = new WinnowOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;
            counter = holder.Value;
            return options;
        }

        private sealed class Counter { public int Value; }

        [Fact]
        public async Task UpdateAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            SeedData(context, 1);
            var products = await context.Products.AsNoTracking().ToListAsync();
            products.AddRange(Pad(500));

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new WinnowOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.UpdateAsync(products, options, cts.Token));
        }

        [Fact]
        public async Task DeleteAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            SeedData(context, 1);
            var products = await context.Products.AsNoTracking().ToListAsync();
            products.AddRange(Pad(500));

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new DeleteOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.DeleteAsync(products, options, cts.Token));
        }

        [Fact]
        public async Task UpsertAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            var products = Pad(500);

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new UpsertOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.UpsertAsync(products, options, cts.Token));
        }
    }

    // --- Task #11: DataAnnotations cache across multiple types ------------

    public class TwoTypeCache
    {
        public sealed class TypeA
        {
            [Required] public string? Name { get; set; }
        }

        public sealed class TypeB
        {
            [Range(1, 10)] public int Score { get; set; }
        }

        [Fact]
        public void WithDataAnnotations_ConfiguredForTwoTypes_EachReportsOwnPropertyName()
        {
            var aOptions = new InsertOptions().WithDataAnnotations<TypeA>();
            var bOptions = new InsertOptions().WithDataAnnotations<TypeB>();

            var aValidator = (ValidatorDelegate<TypeA>)aOptions.Validation!.Validator;
            var bValidator = (ValidatorDelegate<TypeB>)bOptions.Validation!.Validator;

            var aBuffer = new ValidationError[4];
            var aCollector = new ValidationCollector(aBuffer);
            aValidator(new TypeA { Name = null }, ref aCollector);

            var bBuffer = new ValidationError[4];
            var bCollector = new ValidationCollector(bBuffer);
            bValidator(new TypeB { Score = 0 }, ref bCollector);

            aCollector.AsSpan().ToArray()
                .ShouldContain(e => e.PropertyName == nameof(TypeA.Name));
            bCollector.AsSpan().ToArray()
                .ShouldContain(e => e.PropertyName == nameof(TypeB.Score));
        }
    }
}
