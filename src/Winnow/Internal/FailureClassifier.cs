using Microsoft.EntityFrameworkCore;

namespace Winnow.Internal;

internal static class FailureClassifier
{
    internal static FailureReason Classify(Exception ex) => ex switch
    {
        InvalidOperationException => FailureReason.ValidationError,
        DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
        DbUpdateException dbEx when IsDuplicateKeyError(dbEx) => FailureReason.DuplicateKey,
        DbUpdateException => FailureReason.DatabaseConstraint,
        _ => FailureReason.UnknownError
    };

    internal static bool IsDuplicateKeyError(DbUpdateException ex)
    {
        var message = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return message.Contains("unique constraint failed") ||      // SQLite
               message.Contains("violation of primary key") ||      // SQL Server
               message.Contains("violation of unique") ||           // SQL Server
               message.Contains("cannot insert duplicate key") ||   // SQL Server
               message.Contains("duplicate key value violates") ||  // PostgreSQL
               message.Contains("duplicate entry");                 // MySQL
    }

    internal static bool IsTransient(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => false,
        DbUpdateException dbEx => IsTransientDatabaseError(dbEx),
        TimeoutException => true,
        _ => false
    };

    private static bool IsTransientDatabaseError(DbUpdateException ex)
    {
        var message = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return message.Contains("deadlock") ||
               message.Contains("lock timeout") ||
               message.Contains("serialize access") ||
               message.Contains("connection failed") ||
               message.Contains("connection reset") ||
               message.Contains("connection timed out") ||
               message.Contains("command timeout") ||
               message.Contains("database is locked");
    }
}
