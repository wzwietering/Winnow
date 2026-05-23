using System.ComponentModel.DataAnnotations;
using Shouldly;
using Winnow.Internal.Validation;

namespace Winnow.Tests.Internal;

public class DataAnnotationsValidatorFactoryTests
{
    private sealed class AnnotatedEntity
    {
        [Required]
        public string? Name { get; set; }

        [Range(0, 100)]
        public int Score { get; set; }
    }

    /// <summary>
    /// The bug: <c>BuildGetter</c> returned a closure over <see cref="System.Reflection.PropertyInfo.GetValue"/>,
    /// so the "no reflection per entity" claim in the docs was false. After the fix
    /// the getter is a compiled expression tree — its <see cref="System.Delegate.Target"/>
    /// is the framework's <c>System.Runtime.CompilerServices.Closure</c>, not the
    /// compiler-generated user-closure type that captured the <c>PropertyInfo</c>.
    /// </summary>
    [Fact]
    public void BuildGetter_ReturnsCompiledDelegate_NotPropertyInfoClosure()
    {
        var property = typeof(AnnotatedEntity).GetProperty(nameof(AnnotatedEntity.Score))!;

        var getter = DataAnnotationsValidatorFactory.BuildGetter<AnnotatedEntity>(property);

        var targetAssembly = getter.Target?.GetType().Assembly;
        var winnowAssembly = typeof(DataAnnotationsValidatorFactory).Assembly;
        targetAssembly.ShouldNotBe(winnowAssembly,
            "Expected a compiled lambda whose closure lives in System.Linq.Expressions; " +
            "a Winnow-defined closure indicates the old PropertyInfo.GetValue lambda is still in place.");
    }

    [Fact]
    public void BuildGetter_ReadsValueTypeProperty_AsBoxedObject()
    {
        var property = typeof(AnnotatedEntity).GetProperty(nameof(AnnotatedEntity.Score))!;
        var getter = DataAnnotationsValidatorFactory.BuildGetter<AnnotatedEntity>(property);

        getter(new AnnotatedEntity { Score = 42 }).ShouldBe(42);
    }

    [Fact]
    public void BuildGetter_ReadsReferenceTypeProperty()
    {
        var property = typeof(AnnotatedEntity).GetProperty(nameof(AnnotatedEntity.Name))!;
        var getter = DataAnnotationsValidatorFactory.BuildGetter<AnnotatedEntity>(property);

        getter(new AnnotatedEntity { Name = "x" }).ShouldBe("x");
        getter(new AnnotatedEntity { Name = null }).ShouldBeNull();
    }

    // Regression: IValidatableObject.Validate may yield ValidationResult.Success
    // (which is null). The runner used to deref result.ErrorMessage unconditionally,
    // which would NRE the moment a user's validator yielded a Success sentinel
    // (a common pattern when conditionally short-circuiting cross-field checks).
    [Fact]
    public void IValidatableObject_YieldsValidationResultSuccess_DoesNotThrow()
    {
        var errors = WinnowValidatorTester.Validate(
            DataAnnotationsValidatorFactory.Create<EntityYieldingSuccess>(),
            new EntityYieldingSuccess());

        errors.ShouldBeEmpty();
    }

    // Regression: BuildUntyped / CollectAnnotatedProperties pass inherit:true so
    // [Required] on a base property surfaces on a derived entity. Without this,
    // any DataAnnotation defined on a non-leaf type would silently fail to validate.
    [Fact]
    public void Derived_InheritsPropertyAttributeFromBase_FiresRequired()
    {
        var errors = WinnowValidatorTester.Validate(
            DataAnnotationsValidatorFactory.Create<DerivedEntity>(),
            new DerivedEntity { Name = null });

        errors.ShouldContain(e =>
            e.PropertyName == nameof(BaseEntity.Name) && e.Code == nameof(RequiredAttribute));
    }

    private sealed class EntityYieldingSuccess : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return ValidationResult.Success!; // public sentinel that is `null`
        }
    }

    private class BaseEntity
    {
        [Required]
        public string? Name { get; set; }
    }

    private sealed class DerivedEntity : BaseEntity
    {
    }
}
