namespace EfCoreUtils.Internal;

/// <summary>
/// Shared validation methods for many-to-many operations.
/// </summary>
internal static class ManyToManyValidation
{
    /// <summary>
    /// Validates that a collection size does not exceed the configured maximum.
    /// </summary>
    internal static void ValidateCollectionSize(
        string entityTypeName, string navigationName, int itemCount, int maxSize)
    {
        if (maxSize > 0 && itemCount > maxSize)
        {
            throw new InvalidOperationException(
                $"Many-to-many collection '{navigationName}' on entity '{entityTypeName}' " +
                $"has {itemCount} items, exceeding MaxManyToManyCollectionSize of {maxSize}.");
        }
    }
}
