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
}
