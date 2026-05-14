using System.ComponentModel.DataAnnotations;
using Shouldly;
using Winnow.Internal.Validation;

namespace Winnow.Tests.Internal;

public class IncludeNavigationsTests
{
    private sealed class OrderItem
    {
        [Required]
        public string? Sku { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;

        public Order? Parent { get; set; }
    }

    private sealed class Order
    {
        [Required]
        public string? Number { get; set; }

        public List<OrderItem> Items { get; set; } = [];
    }

    private sealed class GrandchildLeaf
    {
        [Required]
        public string? Name { get; set; }
    }

    private sealed class GrandchildMid
    {
        public GrandchildLeaf? Leaf { get; set; }
    }

    private sealed class GrandchildRoot
    {
        [Required]
        public string? Code { get; set; }
        public GrandchildMid? Mid { get; set; }
    }

    private static void Run<TEntity>(List<TEntity> entities, ValidationOptions validation,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        NavigationFilter? navigationFilter = null) where TEntity : class
    {
        PreValidationRunner.Run(entities, validation, recordFailure, navigationFilter, CancellationToken.None);
    }

    private static InsertGraphOptions BuildGraphOptions<TEntity>(bool includeNavigations) where TEntity : class
    {
        var options = new InsertGraphOptions().WithDataAnnotations<TEntity>();
        options.Validation!.IncludeNavigations = includeNavigations;
        return options;
    }

    [Fact]
    public void IncludeNavigationsTrue_ChildValidationFailureIsReported()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: true);

        var order = new Order
        {
            Number = "ABC",
            Items =
            {
                new OrderItem { Sku = "OK", Quantity = 1 },
                new OrderItem { Sku = null, Quantity = 5 },
            }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var failure = failures.ShouldHaveSingleItem();
        failure.Index.ShouldBe(0);
        failure.Errors.ShouldContain(err =>
            err.PropertyName == "Items[1].Sku" && err.Message.Contains("Sku"));
    }

    [Fact]
    public void IncludeNavigationsFalse_ChildValidationIsSkipped()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: false);

        var order = new Order
        {
            Number = "ABC",
            Items = { new OrderItem { Sku = null, Quantity = 5 } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void IncludeNavigationsTrue_WithTypedDelegate_ThrowsInvalidOperationException()
    {
        var options = new InsertGraphOptions();
        options.WithValidation<Order>((Order o, ref ValidationCollector c) => { });
        options.Validation!.IncludeNavigations = true;

        var ex = Should.Throw<InvalidOperationException>(() =>
            Run([new Order { Number = "x" }], options.Validation!, (_, _, _) => { }));

        ex.Message.ShouldContain("IncludeNavigations");
        ex.Message.ShouldContain("DataAnnotations");
    }

    [Fact]
    public void IncludeNavigationsTrue_CycleInGraph_DoesNotInfiniteLoop()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: true);

        var order = new Order { Number = "ABC" };
        var item = new OrderItem { Sku = "X", Quantity = 1, Parent = order };
        order.Items.Add(item);

        var failures = new List<(int, string, IReadOnlyList<ValidationError>)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        failures.ShouldBeEmpty();
    }

    // GraphValidationOptions lives only on GraphOptionsBase, so the compile-time
    // type system prevents IncludeNavigations from being attached to a flat
    // operation. The "silent no-op on flat" footgun no longer exists.

    [Fact]
    public void IncludeNavigationsTrue_NavigationFilterExcludesItems_ChildrenAreNotValidated()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: true);

        var filter = NavigationFilter.Exclude()
            .Navigation<Order>(o => o.Items)
            .Build();

        var order = new Order
        {
            Number = "OK",
            Items = { new OrderItem { Sku = null, Quantity = 5 } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)),
            navigationFilter: filter);

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void IncludeNavigationsTrue_IncludeFilterListsItems_OtherNavigationsSkipped()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: true);

        var filter = NavigationFilter.Include()
            .Navigation<Order>(o => o.Items)
            .Build();

        var order = new Order
        {
            Number = "OK",
            Items = { new OrderItem { Sku = null, Quantity = 5 } }
        };

        var failures = new List<(int, string, IReadOnlyList<ValidationError>)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)),
            navigationFilter: filter);

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Items[0].Sku");
    }

    [Fact]
    public void IncludeNavigationsTrue_AnnotatedGrandchildBehindUnannotatedIntermediate_StillValidated()
    {
        var options = BuildGraphOptions<GrandchildRoot>(includeNavigations: true);

        var root = new GrandchildRoot
        {
            Code = "C",
            Mid = new GrandchildMid { Leaf = new GrandchildLeaf { Name = null } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([root], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Mid.Leaf.Name");
    }

    [Fact]
    public void IncludeNavigationsTrue_RootValidationStillRuns()
    {
        var options = BuildGraphOptions<Order>(includeNavigations: true);

        var order = new Order { Number = null, Items = { new OrderItem { Sku = "OK", Quantity = 1 } } };

        var failures = new List<(int, string, IReadOnlyList<ValidationError>)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Number");
    }
}
