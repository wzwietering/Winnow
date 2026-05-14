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

    private static void Run(List<Order> entities, ValidationOptions options,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure)
    {
        PreValidationRunner.Run(entities, options, recordFailure, CancellationToken.None);
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
