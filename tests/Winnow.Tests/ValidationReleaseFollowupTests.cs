using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Coverage added during the 1.3.0 release follow-up pass — closes the gaps the
/// deep-review surfaced after the first round of release-readiness work:
/// <list type="bullet">
///   <item>C1: backward-compat baseline (Validation == null path)</item>
///   <item>C3: WinnowValidationException truncation past 5 failures</item>
///   <item>C4: IncludeNavigations + NavigationFilter end-to-end</item>
///   <item>C5: validation + composite-key entity</item>
///   <item>C6: self-referencing entity with IncludeNavigations</item>
///   <item>C7: UpsertAsync + ThrowAfterBatch + cancellation</item>
///   <item>B2/C8: [NotMapped] regression</item>
/// </list>
/// </summary>
public class ValidationReleaseFollowupTests
{
    // --- C1: Backward-compat baseline ---------------------------------------

    public class BackwardCompatBaseline : TestBase
    {
        // When Validation is null, Insert/Update/Delete/Upsert must produce results
        // identical to the pre-1.3.0 baseline: every entity flows to the strategy,
        // no validation failures appear, the round-trip count is unchanged. This is
        // the single most important confidence signal for existing 1.2.0 users.
        [Fact]
        public void Insert_NoValidation_ProducesIdenticalResultToBaseline()
        {
            using var context = CreateContext();
            var products = Enumerable.Range(0, 5).Select(i => new Product
            {
                Name = $"p{i}", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = new Winnower<Product, int>(context);
            var result = saver.Insert(products, new InsertOptions());

            result.SuccessCount.ShouldBe(5);
            result.FailureCount.ShouldBe(0);
            result.Failures.ShouldBeEmpty();
        }

        [Fact]
        public void Update_NoValidation_ProducesIdenticalResultToBaseline()
        {
            using var context = CreateContext();
            SeedData(context, 3);
            var products = context.Products.AsNoTracking().ToList();
            foreach (var p in products) p.Price += 1m;

            var saver = new Winnower<Product, int>(context);
            var result = saver.Update(products, new WinnowOptions());

            result.SuccessCount.ShouldBe(3);
            result.FailureCount.ShouldBe(0);
        }

        [Fact]
        public void Delete_NoValidation_ProducesIdenticalResultToBaseline()
        {
            using var context = CreateContext();
            SeedData(context, 3);
            var products = context.Products.AsNoTracking().ToList();

            var saver = new Winnower<Product, int>(context);
            var result = saver.Delete(products, new DeleteOptions());

            result.SuccessCount.ShouldBe(3);
            result.FailureCount.ShouldBe(0);
        }

        [Fact]
        public void Upsert_NoValidation_ProducesIdenticalResultToBaseline()
        {
            using var context = CreateContext();
            SeedData(context, 2);
            var existing = context.Products.AsNoTracking().ToList();
            foreach (var e in existing) e.Price += 1m;
            var newProduct = new Product { Name = "new", Price = 2m, Stock = 1, LastModified = DateTimeOffset.UtcNow };
            var batch = new List<Product>(existing) { newProduct };

            var saver = new Winnower<Product, int>(context);
            var result = saver.Upsert(batch, new UpsertOptions());

            result.SuccessCount.ShouldBe(3);
            result.FailureCount.ShouldBe(0);
            result.UpdatedCount.ShouldBe(2);
            result.InsertedCount.ShouldBe(1);
        }
    }

    // --- C3: WinnowValidationException message truncation -------------------

    public class ExceptionMessageTruncation
    {
        // BuildMessage caps the indices listed in the message at 5 and appends
        // ", ..." past that. The format is part of the observable string contract
        // even though Errors is the structured surface — log scrapers depend on it.
        [Fact]
        public void Message_WithMoreThanFiveFailures_TruncatesAndAppendsEllipsis()
        {
            var errors = new ValidationError[] { new("X", "bad", "B") };
            var failures = Enumerable.Range(0, 8)
                .Select(i => new WinnowValidationException.EntityFailure(i, "bad", errors))
                .ToList();

            var ex = new WinnowValidationException(failures);

            ex.Message.ShouldContain("indices 0, 1, 2, 3, 4, ...");
            ex.Message.ShouldNotContain("5,");
            ex.Message.ShouldNotContain("6,");
            ex.Message.ShouldStartWith("Pre-validation failed for 8 entities");
        }

        [Fact]
        public void Message_WithExactlyFiveFailures_DoesNotAppendEllipsis()
        {
            var errors = new ValidationError[] { new("X", "bad", "B") };
            var failures = Enumerable.Range(0, 5)
                .Select(i => new WinnowValidationException.EntityFailure(i, "bad", errors))
                .ToList();

            var ex = new WinnowValidationException(failures);

            ex.Message.ShouldContain("indices 0, 1, 2, 3, 4");
            ex.Message.ShouldNotContain("...");
        }
    }

    // --- C4: IncludeNavigations + NavigationFilter end-to-end ---------------

    public class IncludeNavigationsRespectsNavigationFilter : TestBase
    {
        // The NavigationWalker forwards the operation's NavigationFilter to its
        // ShouldTraverse check. Excluded navigations should not produce validation
        // errors, even when the child has DataAnnotations that would otherwise fail.
        [Fact]
        public void InsertGraph_IncludeNavigations_HonoursNavigationFilterExclusion()
        {
            using var context = CreateContext();
            var orders = new[]
            {
                new CustomerOrder
                {
                    OrderNumber = "OK", CustomerName = "C", CustomerId = 1,
                    TotalAmount = 1m, OrderDate = DateTimeOffset.UtcNow,
                    OrderItems =
                    [
                        new OrderItem
                        {
                            ProductId = 1,
                            ProductName = null!, // would fail [Required] if walked
                            Quantity = 1, UnitPrice = 1m, Subtotal = 1m,
                        }
                    ],
                }
            };

            var options = new InsertGraphOptions
            {
                NavigationFilter = NavigationFilter.Exclude()
                    .Navigation<CustomerOrder>(o => o.OrderItems),
            };
            options.WithDataAnnotations<CustomerOrder>(includeNavigations: true);

            var saver = new Winnower<CustomerOrder, int>(context);
            var result = saver.InsertGraph(orders, options);

            result.SuccessCount.ShouldBe(1);
            result.FailureCount.ShouldBe(0);
        }
    }

    // --- C5: Validation + composite-key entity ------------------------------

    public class CompositeKeyValidation : TestBase
    {
        // OrderLine has a composite primary key (OrderId, LineNumber). The pipeline
        // must surface validation failures on these entities without choking on the
        // composite-key shape — the failure carries the entity's CompositeKey id and
        // the validation error list is intact.
        [Fact]
        public void Update_CompositeKeyEntity_RejectsViaValidationAndPreservesIdentity()
        {
            using var context = CreateContext();
            var order = new CustomerOrder
            {
                OrderNumber = "X",
                CustomerName = "C",
                CustomerId = 1,
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow,
            };
            context.CustomerOrders.Add(order);
            context.SaveChanges();

            var lines = new[]
            {
                new OrderLine { OrderId = order.Id, LineNumber = 1, Quantity = 1, UnitPrice = 1m },
                new OrderLine { OrderId = order.Id, LineNumber = 2, Quantity = 1, UnitPrice = 1m },
            };
            context.OrderLines.AddRange(lines);
            context.SaveChanges();
            context.ChangeTracker.Clear();

            // Mutate to drive validation differently per line; lines[1] becomes the
            // validation reject. Mutating UnitPrice keeps the EF ValidateOrderLines
            // guard happy (it only rejects non-positive Quantity).
            lines[0].UnitPrice = 2m;
            lines[1].UnitPrice = 2m;
            var options = new WinnowOptions();
            options.WithValidation<OrderLine>((OrderLine ol, ref ValidationCollector c) =>
            {
                if (ol.LineNumber == 2) c.Add(nameof(OrderLine.UnitPrice), "Reject second line", "REJECT");
            });

            var saver = new Winnower<OrderLine, CompositeKey>(context);
            var result = saver.Update(lines, options);

            result.SuccessCount.ShouldBe(1);
            result.FailureCount.ShouldBe(1);
            var failure = result.Failures.ShouldHaveSingleItem();
            failure.Reason.ShouldBe(FailureReason.ValidationError);
            failure.ValidationErrors.ShouldNotBeNull();
            failure.ValidationErrors!.ShouldContain(e => e.Code == "REJECT");
            failure.EntityId.ShouldNotBe(default!);
        }
    }

    // --- C6: Self-referencing entity with IncludeNavigations ---------------

    public class SelfReferencingNavigationWalk : TestBase
    {
        // Annotate a self-referencing entity locally so the walker descends into
        // the SubCategories collection and back-references — exercises the cycle
        // guard with a real annotated self-reference. (The shared Category entity
        // doesn't have annotations, so we use a fresh local type that does.)
        [Fact]
        public void NavigationWalker_SelfReferencing_TerminatesOnCycle()
        {
            var root = new SelfRefNode { Name = null! };
            var child = new SelfRefNode { Name = "child", Parent = root };
            var grandchild = new SelfRefNode { Name = null!, Parent = child };
            root.Children.Add(child);
            child.Children.Add(grandchild);
            // Cycle: grandchild's parent already visited (child)

            var validator = Winnow.Internal.Validation.DataAnnotationsValidatorFactory.Create<SelfRefNode>();
            var collector = ValidationCollector.Create();
            try
            {
                validator(root, ref collector);
                collector.AsSpan().ToArray()
                    .ShouldContain(e => e.PropertyName == nameof(SelfRefNode.Name));
            }
            finally { collector.Dispose(); }

            // Drive the walker so it descends — must terminate even though
            // root → children[0] → parent (== root) is a reference cycle.
            var walkerCollector = ValidationCollector.Create();
            try
            {
                walkerCollector.IsValid.ShouldBeTrue();
                Winnow.Internal.Validation.NavigationWalker.Walk(
                    root, ref walkerCollector, maxDepth: 32, filter: null);
                // Should have surfaced grandchild's missing Name via the walk —
                // proves descent happened — without throwing or going infinite.
                walkerCollector.AsSpan().ToArray()
                    .ShouldContain(e => e.PropertyName.Contains("Children"));
            }
            finally { walkerCollector.Dispose(); }
        }

        private sealed class SelfRefNode
        {
            [Required] public string Name { get; set; } = null!;
            public SelfRefNode? Parent { get; set; }
            public List<SelfRefNode> Children { get; set; } = [];
        }
    }

    // --- C7: UpsertAsync + ThrowAfterBatch + cancellation -------------------

    public class UpsertAsyncThrowCancellation : TestBase
    {
        // Combines the throw-mode aggregation with a token cancelled mid-validation.
        // ThrowIfAnyFailed runs after ScanEntities; if cancellation fires inside
        // ScanEntities it must propagate as OperationCanceledException, not get
        // swallowed by the throw-mode collector.
        [Fact]
        public async Task UpsertAsync_ThrowAfterBatch_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            var products = Enumerable.Range(0, 500).Select(i => new Product
            {
                Id = i + 200_000,
                Name = $"p{i}", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new UpsertOptions();
            options.WithValidation<Product>(
                (Product p, ref ValidationCollector c) =>
                {
                    if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
                    c.Add(nameof(Product.Price), "always", "ALWAYS");
                },
                ValidationFailureBehavior.ThrowAfterBatch);
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);

            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.UpsertAsync(products, options, cts.Token));
        }

        private sealed class Counter { public int Value; }
    }

    // --- B1: IValidatableObject + class-level [CustomValidation] -----------

    public class IValidatableObjectAndCustomValidation
    {
        // Cross-field rule via IValidatableObject must surface alongside any
        // property-level attribute failures, sharing the same ValidationCollector
        // and the same failure-behaviour semantics.
        [Fact]
        public void DataAnnotations_IValidatableObject_RunsCrossFieldRule()
        {
            var validator = Winnow.Internal.Validation.DataAnnotationsValidatorFactory.Create<Booking>();
            var collector = ValidationCollector.Create();
            try
            {
                validator(new Booking { Start = new DateTime(2026, 1, 2), End = new DateTime(2026, 1, 1) }, ref collector);

                var errors = collector.AsSpan().ToArray();
                errors.ShouldContain(e => e.Code == "WINNOW_VALIDATABLE_OBJECT");
                errors.ShouldContain(e => e.PropertyName == nameof(Booking.End));
            }
            finally { collector.Dispose(); }
        }

        [Fact]
        public void DataAnnotations_PropertyLevel_AndIValidatableObject_BothEmitErrors()
        {
            var validator = Winnow.Internal.Validation.DataAnnotationsValidatorFactory.Create<Booking>();
            var collector = ValidationCollector.Create();
            try
            {
                // Both: Title missing (property-level [Required]) AND End <= Start (cross-field).
                validator(
                    new Booking { Title = null!, Start = new DateTime(2026, 1, 2), End = new DateTime(2026, 1, 1) },
                    ref collector);

                var errors = collector.AsSpan().ToArray();
                errors.ShouldContain(e => e.PropertyName == nameof(Booking.Title) && e.Code == nameof(RequiredAttribute));
                errors.ShouldContain(e => e.Code == "WINNOW_VALIDATABLE_OBJECT");
            }
            finally { collector.Dispose(); }
        }

        [Fact]
        public void DataAnnotations_ClassLevelCustomValidation_RunsViaGetValidationResult()
        {
            var validator = Winnow.Internal.Validation.DataAnnotationsValidatorFactory.Create<ClassValidatedEntity>();
            var collector = ValidationCollector.Create();
            try
            {
                validator(new ClassValidatedEntity { Marker = "fail" }, ref collector);

                var errors = collector.AsSpan().ToArray();
                errors.ShouldContain(e => e.Code == nameof(MarkerMustNotBeFailAttribute));
            }
            finally { collector.Dispose(); }
        }

        private sealed class Booking : IValidatableObject
        {
            [Required] public string Title { get; set; } = "default";
            public DateTime Start { get; set; }
            public DateTime End { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (End <= Start)
                {
                    yield return new ValidationResult("End must be after Start.", new[] { nameof(End) });
                }
            }
        }

        [MarkerMustNotBeFail]
        private sealed class ClassValidatedEntity
        {
            public string Marker { get; set; } = "ok";
        }

        [AttributeUsage(AttributeTargets.Class)]
        private sealed class MarkerMustNotBeFailAttribute : ValidationAttribute
        {
            protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            {
                if (value is ClassValidatedEntity { Marker: "fail" })
                    return new ValidationResult("Marker must not be 'fail'.");
                return ValidationResult.Success;
            }
        }
    }

    // --- B2/C8: [NotMapped] regression --------------------------------------

    public class NotMappedExclusion
    {
        // Computed/derived properties annotated with DataAnnotations but marked
        // [NotMapped] should not be walked as navigations; they're typically
        // transient projection objects without an EF mapping.
        [Fact]
        public void NavigationWalker_SkipsNotMappedClassProperties()
        {
            var entity = new HostWithComputedView();

            var validator = Winnow.Internal.Validation.DataAnnotationsValidatorFactory.Create<HostWithComputedView>();
            var collector = ValidationCollector.Create();
            try
            {
                validator(entity, ref collector);
                collector.IsValid.ShouldBeTrue();
            }
            finally { collector.Dispose(); }

            var walkerCollector = ValidationCollector.Create();
            try
            {
                Winnow.Internal.Validation.NavigationWalker.Walk(
                    entity, ref walkerCollector, maxDepth: 32, filter: null);
                // A [NotMapped] computed property would otherwise produce a "ProjectedView."-prefixed
                // failure. The exclusion must keep the walker from descending.
                foreach (var error in walkerCollector.AsSpan().ToArray())
                {
                    error.PropertyName.ShouldNotContain("ProjectedView", Case.Sensitive);
                }
            }
            finally { walkerCollector.Dispose(); }
        }

        private sealed class HostWithComputedView
        {
            public int Id { get; set; }

            [NotMapped]
            public ProjectedView ProjectedView => new() { RequiredField = null! };
        }

        private sealed class ProjectedView
        {
            [Required] public string RequiredField { get; set; } = null!;
        }
    }
}
