namespace EfCoreUtils.Internal;

/// <summary>
/// Helper for checking if values are their type's default value.
/// Used for key validation and new entity detection.
/// </summary>
internal static class DefaultValueHelper
{
    private static readonly Dictionary<Type, Func<object, bool>> Checkers = new()
    {
        { typeof(int), v => v is int i && i == 0 },
        { typeof(int?), v => v is int i && i == 0 },
        { typeof(long), v => v is long l && l == 0 },
        { typeof(long?), v => v is long l && l == 0 },
        { typeof(short), v => v is short s && s == 0 },
        { typeof(short?), v => v is short s && s == 0 },
        { typeof(byte), v => v is byte b && b == 0 },
        { typeof(byte?), v => v is byte b && b == 0 },
        { typeof(Guid), v => v is Guid g && g == Guid.Empty },
        { typeof(Guid?), v => v is Guid g && g == Guid.Empty },
        { typeof(string), v => v is string s && string.IsNullOrEmpty(s) },
        { typeof(CompositeKey), v => v is CompositeKey ck && ck.IsAllDefaults() },
    };

    internal static bool IsDefault(object? value, Type type)
    {
        if (value == null)
        {
            return true;
        }

        return Checkers.TryGetValue(type, out var checker) && checker(value);
    }

    internal static bool IsDefault(object? value)
    {
        if (value == null)
        {
            return true;
        }

        var type = value.GetType();
        if (Checkers.TryGetValue(type, out var checker))
        {
            return checker(value);
        }

        if (type.IsValueType)
        {
            return value.Equals(Activator.CreateInstance(type));
        }

        return false;
    }
}
