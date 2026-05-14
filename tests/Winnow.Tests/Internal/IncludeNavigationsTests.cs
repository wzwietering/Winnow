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
    public void IncludeNavigationsTrue_WithTypedDelegate_ThrowsAtConfigurationTime()
    {
        var options = new InsertGraphOptions();
        options.WithValidation<Order>((Order o, ref ValidationCollector c) => { });

        var ex = Should.Throw<InvalidOperationException>(() =>
            options.Validation!.IncludeNavigations = true);

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

    private sealed class DiamondLeaf
    {
        [Required] public string? Name { get; set; }
    }

    private sealed class DiamondRoot
    {
        [Required] public string? RootName { get; set; }
        public DiamondLeaf? PathA { get; set; }
        public DiamondLeaf? PathB { get; set; }
    }

    [Fact]
    public void IncludeNavigationsTrue_SharedReference_ValidatesLeafOnce()
    {
        var options = BuildGraphOptions<DiamondRoot>(includeNavigations: true);

        var sharedLeaf = new DiamondLeaf { Name = null };
        var root = new DiamondRoot { RootName = "R", PathA = sharedLeaf, PathB = sharedLeaf };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([root], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.Count(e => e.PropertyName.EndsWith(".Name")).ShouldBe(1);
    }

    private sealed class CollectionHolder
    {
        public List<OrderItem?> Items { get; set; } = [];
    }

    [Fact]
    public void IncludeNavigationsTrue_NullCollectionElement_DoesNotThrowAndStillValidatesOthers()
    {
        var options = BuildGraphOptions<CollectionHolder>(includeNavigations: true);

        var holder = new CollectionHolder
        {
            Items = { null, new OrderItem { Sku = null, Quantity = 1 } }
        };

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([holder], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(e => e.PropertyName == "Items[1].Sku");
    }

    private sealed class ChainNode
    {
        [Required] public string? Name { get; set; }
        public ChainNode? Next { get; set; }
    }

    [Fact]
    public void IncludeNavigationsTrue_DeepLinearChain_StopsAtDepthLimit()
    {
        var options = BuildGraphOptions<ChainNode>(includeNavigations: true);
        options.Validation!.MaxNavigationDepth = 5;

        var head = new ChainNode { Name = "0" };
        var current = head;
        for (int i = 1; i < 64; i++)
        {
            var next = new ChainNode { Name = $"{i}" };
            current.Next = next;
            current = next;
        }

        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Run([head], options.Validation!, (i, m, e) => failures.Add((i, m, e)));

        var (_, _, errors) = failures.ShouldHaveSingleItem();
        errors.ShouldContain(e => e.Code == NavigationWalker.DepthLimitErrorCode);
    }

    [Fact]
    public void GraphValidationOptions_MaxNavigationDepth_RejectsNonPositive()
    {
        var options = new InsertGraphOptions().WithDataAnnotations<ChainNode>();
        Should.Throw<ArgumentOutOfRangeException>(() => options.Validation!.MaxNavigationDepth = 0);
        Should.Throw<ArgumentOutOfRangeException>(() => options.Validation!.MaxNavigationDepth = -1);
    }
}
