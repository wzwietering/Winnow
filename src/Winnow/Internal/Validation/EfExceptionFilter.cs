namespace Winnow.Internal.Validation;

/// <summary>
/// Narrows exception suppression to <see cref="InvalidOperationException"/>
/// instances raised by EF Core itself (shadow primary keys, model
/// misconfiguration) rather than by user code. Programmer errors
/// (<see cref="NullReferenceException"/>, <see cref="InvalidCastException"/>,
/// etc.) and user-delegate exceptions must propagate so they surface as real
/// bugs instead of collapsing into default-keyed failures with no diagnostic
/// signal.
/// </summary>
internal static class EfExceptionFilter
{
    internal static bool IsEntityFrameworkInvalidOperation(InvalidOperationException ex)
    {
        // TargetSite reflects the method that threw; checking its declaring
        // assembly pins suppression to exceptions actually originating in EF
        // Core, not in any user delegate that happens to surface
        // InvalidOperationException.
        var assemblyName = ex.TargetSite?.DeclaringType?.Assembly?.GetName().Name;
        return assemblyName is not null
            && assemblyName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
    }
}
