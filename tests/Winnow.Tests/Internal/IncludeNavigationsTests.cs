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

    // Bug 5 scaffold: intermediate type has no annotations, leaf does. Before the
    // fix, the walker excluded Mid from BuildNavigations because Mid was
    // "unannotated" and never recursed into Leaf.
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

    private static void Run(List<Order> entities, ValidationOptions options,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        bool isGraphOperation = true,
        NavigationFilter? navigationFilter = null)
    {
        PreValidationRunner.Run(entities, options, recordFailure, isGraphOperation, navigationFilter, CancellationToken.None);
    }

    [Fact]
    public void IncludeNavigationsTrue_ChildValidationFailureIsReported()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

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
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = false;

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
        var options = new WinnowOptions();
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
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

        var order = new Order { Number = "ABC" };
        var item = new OrderItem { Sku = "X", Quantity = 1, Parent = order };
        order.Items.Add(item);

        var failures = new List<(int, string, IReadOnlyList<ValidationError>)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        failures.ShouldBeEmpty();
    }

    // Bug 3 proof: ValidationOptions.IncludeNavigations doc says "Has no effect on
    // flat (non-graph) operations." Before the fix, the runner unconditionally walked
    // navigations whenever the flag was true, regardless of operation kind.
    [Fact]
    public void IncludeNavigationsTrue_FlatOperation_DoesNotWalkChildren()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

        var order = new Order
        {
            Number = "ABC",
            Items = { new OrderItem { Sku = null, Quantity = 5 } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)), isGraphOperation: false);

        failures.ShouldBeEmpty();
    }

    // Bug 3 corollary: IncludeNavigations on a typed delegate normally throws to
    // signal misconfiguration, but on a flat operation the flag is silently ignored
    // so the throw should also be skipped — a typed-delegate config that "accidentally"
    // has IncludeNavigations=true should not fail when used on a flat operation.
    [Fact]
    public void IncludeNavigationsTrue_FlatOperationWithTypedDelegate_DoesNotThrow()
    {
        var options = new WinnowOptions();
        options.WithValidation<Order>((Order o, ref ValidationCollector c) => { });
        options.Validation!.IncludeNavigations = true;

        Should.NotThrow(() => Run([new Order { Number = "x" }], options.Validation!, (_, _, _) => { }, isGraphOperation: false));
    }

    // Bug 4 proof: GraphOptionsBase.Validation doc claims the walk respects
    // NavigationFilter. Before the fix NavigationWalker performed pure reflection
    // and ignored the filter, so excluded navigations were still validated.
    [Fact]
    public void IncludeNavigationsTrue_NavigationFilterExcludesItems_ChildrenAreNotValidated()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

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
            isGraphOperation: true, navigationFilter: filter);

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void IncludeNavigationsTrue_IncludeFilterListsItems_OtherNavigationsSkipped()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

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
            isGraphOperation: true, navigationFilter: filter);

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Items[0].Sku");
    }

    // Bug 5 proof: the walker treats a navigation as "skip" when its element type
    // has no DataAnnotations of its own — but that skip silently drops every
    // annotation reachable through that intermediate. The reachable annotations
    // on GrandchildLeaf.Name must still be evaluated.
    [Fact]
    public void IncludeNavigationsTrue_AnnotatedGrandchildBehindUnannotatedIntermediate_StillValidated()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<GrandchildRoot>();
        options.Validation!.IncludeNavigations = true;

        var root = new GrandchildRoot
        {
            Code = "C",
            Mid = new GrandchildMid { Leaf = new GrandchildLeaf { Name = null } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        PreValidationRunner.Run([root], options.Validation!,
            (i, m, e) => failures.Add((i, m, e)),
            isGraphOperation: true, navigationFilter: null, CancellationToken.None);

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Mid.Leaf.Name");
    }

    [Fact]
    public void IncludeNavigationsTrue_RootValidationStillRuns()
    {
        var options = new WinnowOptions();
        options.WithDataAnnotations<Order>();
        options.Validation!.IncludeNavigations = true;

        var order = new Order { Number = null, Items = { new OrderItem { Sku = "OK", Quantity = 1 } } };

        var failures = new List<(int, string, IReadOnlyList<ValidationError>)>();
        Run([order], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(err => err.PropertyName == "Number");
    }
}
