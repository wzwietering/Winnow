using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Internal.Validation;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests.Internal.Validation;

public class EfExceptionFilterTests : TestBase
{
    [Fact]
    public void ReturnsFalse_WhenTargetSiteIsNull()
    {
        // A freshly-constructed exception that was never thrown has no TargetSite.
        // The filter must refuse to suppress — without a stack frame to attribute the
        // throw to, we cannot tell whether it originated in EF Core or user code.
        var ex = new InvalidOperationException("not thrown");

        EfExceptionFilter.IsEntityFrameworkInvalidOperation(ex).ShouldBeFalse();
    }

    [Fact]
    public void ReturnsTrue_WhenThrownFromEntityFrameworkCore()
    {
        // context.Entry(<unknown type>) throws InvalidOperationException with TargetSite
        // in the Microsoft.EntityFrameworkCore assembly — exactly the shadow-key /
        // model-misconfiguration shape SafeReadKey is meant to suppress.
        using var context = CreateContext();
        InvalidOperationException? captured = null;
        try { context.Entry(new UnknownEntity()); }
        catch (InvalidOperationException ex) { captured = ex; }

        captured.ShouldNotBeNull();
        EfExceptionFilter.IsEntityFrameworkInvalidOperation(captured!).ShouldBeTrue();
    }

    [Fact]
    public void ReturnsFalse_WhenThrownFromUserCode()
    {
        // Any InvalidOperationException originating in the test assembly (i.e. user code)
        // must propagate — collapsing it into a default-keyed failure would hide real
        // programmer bugs behind a silent absorption.
        InvalidOperationException? captured = null;
        try { ThrowFromUserCode(); }
        catch (InvalidOperationException ex) { captured = ex; }

        captured.ShouldNotBeNull();
        EfExceptionFilter.IsEntityFrameworkInvalidOperation(captured!).ShouldBeFalse();
    }

    [Fact]
    public void ReturnsFalse_WhenThrownFromFrameworkAssemblyOtherThanEfCore()
    {
        // Enumerable.First on an empty sequence throws InvalidOperationException with
        // TargetSite in System.Linq — confirms the filter doesn't broaden to "any
        // framework-thrown InvalidOperationException." The suppression must be
        // narrow to EF Core specifically.
        InvalidOperationException? captured = null;
        try { Enumerable.Empty<int>().First(); }
        catch (InvalidOperationException ex) { captured = ex; }

        captured.ShouldNotBeNull();
        EfExceptionFilter.IsEntityFrameworkInvalidOperation(captured!).ShouldBeFalse();
    }

    private static void ThrowFromUserCode()
    {
        throw new InvalidOperationException("user code");
    }

    private sealed class UnknownEntity
    {
        public int Id { get; set; }
    }
}
