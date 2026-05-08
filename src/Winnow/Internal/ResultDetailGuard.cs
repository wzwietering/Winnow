namespace Winnow.Internal;

/// <summary>
/// Builds the exception thrown when a result property is accessed at a
/// <see cref="ResultDetail"/> level below the threshold required to capture
/// its data.
/// </summary>
internal static class ResultDetailGuard
{
    internal static InvalidOperationException NotCaptured(
        string propertyName,
        ResultDetail required,
        ResultDetail actual,
        string? alternative = null)
    {
        var fallback = alternative is null
            ? string.Empty
            : $", or use {alternative} instead";
        var message =
            $"{propertyName} requires ResultDetail.{required} or higher; current is ResultDetail.{actual}. " +
            $"Raise the ResultDetail on the operation options{fallback}.";
        return new InvalidOperationException(message);
    }
}
