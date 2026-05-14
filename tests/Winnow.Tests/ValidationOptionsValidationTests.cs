using System.ComponentModel.DataAnnotations;
using Shouldly;
using Winnow.Tests.Entities;

namespace Winnow.Tests;

public class ValidationOptionsValidationTests
{
    [Fact]
    public void WithValidation_NullOptions_Throws()
    {
        WinnowOptions? options = null;
        Should.Throw<ArgumentNullException>(() =>
            options!.WithValidation<Product>((Product _, ref ValidationCollector _) => { }));
    }

    [Fact]
    public void WithValidation_NullDelegate_Throws()
    {
        var options = new WinnowOptions();
        Should.Throw<ArgumentNullException>(() =>
            options.WithValidation<Product>(null!));
    }

    [Fact]
    public void WithValidation_AssignsValidationProperty()
    {
        var options = new WinnowOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        options.Validation.ShouldNotBeNull();
        options.Validation!.FailureBehavior.ShouldBe(ValidationFailureBehavior.RecordAsFailure);
        options.Validation.CancellationCheckInterval.ShouldBe(ValidationOptions.DefaultCancellationCheckInterval);
    }

    [Fact]
    public void WithDataAnnotations_OnGraphOptions_DefaultsIncludeNavigationsToFalse()
    {
        var options = new InsertGraphOptions().WithDataAnnotations<Product>();
        options.Validation.ShouldNotBeNull();
        options.Validation!.IncludeNavigations.ShouldBeFalse();
    }

    [Fact]
    public void WithValidation_GraphOptionsOverload_AssignsValidation()
    {
        var options = new GraphOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        options.Validation.ShouldNotBeNull();
    }

    [Fact]
    public void WithValidation_CalledTwice_LastCallWins()
    {
        var options = new InsertOptions();
        ValidatorDelegate<Product> first = (Product _, ref ValidationCollector _) => { };
        ValidatorDelegate<Product> second = (Product _, ref ValidationCollector c) => c.Add("X", "second");

        options.WithValidation(first);
        options.WithValidation(second);

        // Apply and confirm the second validator runs.
        var validator = (ValidatorDelegate<Product>)options.Validation!.Validator;
        var buffer = new ValidationError[4];
        var collector = new ValidationCollector(buffer);
        validator(new Product(), ref collector);
        collector.Count.ShouldBe(1);
        collector.AsSpan()[0].Message.ShouldBe("second");
    }

    [Fact]
    public void CancellationCheckInterval_NegativeOrZero_Throws()
    {
        var options = new WinnowOptions().WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        Should.Throw<ArgumentOutOfRangeException>(() => options.Validation!.CancellationCheckInterval = 0);
        Should.Throw<ArgumentOutOfRangeException>(() => options.Validation!.CancellationCheckInterval = -1);
    }

    [Fact]
    public void WithDataAnnotations_AssignsValidationProperty()
    {
        var options = new WinnowOptions().WithDataAnnotations<AnnotatedEntity>();
        options.Validation.ShouldNotBeNull();
    }

    [Fact]
    public void WithDataAnnotations_ValidatorReportsRequiredAndRangeFailures()
    {
        var options = new WinnowOptions().WithDataAnnotations<AnnotatedEntity>();
        var validator = (ValidatorDelegate<AnnotatedEntity>)options.Validation!.Validator;

        var entity = new AnnotatedEntity { Name = null!, Score = -1 };
        var buffer = new ValidationError[8];
        var collector = new ValidationCollector(buffer);
        validator(entity, ref collector);

        collector.Count.ShouldBeGreaterThanOrEqualTo(2);
        var msgs = collector.AsSpan().ToArray().Select(e => e.PropertyName).ToList();
        msgs.ShouldContain("Name");
        msgs.ShouldContain("Score");
    }

    // Small entity type used purely for DataAnnotations cache tests.
    public sealed class AnnotatedEntity
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(0, 100)]
        public int Score { get; set; }
    }
}
