using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Internal;

internal static class FailureClassifier
{
    internal static FailureReason Classify(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };
}
