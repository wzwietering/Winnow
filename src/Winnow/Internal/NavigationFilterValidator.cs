using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal;

/// <summary>
/// Validates that a NavigationFilter does not conflict with boolean option flags.
/// </summary>
internal static class NavigationFilterValidator
{
    internal static void Validate(
        NavigationFilter? filter, IModel model, bool includeReferences, bool includeManyToMany)
    {
        if (filter == null)
        {
            return;
        }

        foreach (var (entityType, navigationNames) in filter.Rules)
        {
            var efType = model.FindEntityType(entityType);
            if (efType == null)
            {
                continue;
            }

            foreach (var navName in navigationNames)
            {
                ValidateNavigationExists(efType, navName);

                if (filter.IsIncludeMode)
                {
                    ValidateFlagConflicts(efType, navName, includeReferences, includeManyToMany);
                }
            }
        }
    }

    private static void ValidateNavigationExists(IEntityType efType, string navName)
    {
        var navigation = efType.FindNavigation(navName);
        var skipNavigation = efType.FindSkipNavigation(navName);

        if (navigation == null && skipNavigation == null)
        {
            throw new InvalidOperationException(
                $"NavigationFilter references '{navName}' on '{efType.ClrType.Name}', " +
                $"but no such navigation property exists in the EF model.");
        }
    }

    private static void ValidateFlagConflicts(
        IEntityType efType, string navName, bool includeReferences, bool includeManyToMany)
    {
        var navigation = efType.FindNavigation(navName);
        var skipNavigation = efType.FindSkipNavigation(navName);

        if (navigation != null && !navigation.IsCollection && !includeReferences)
        {
            throw new InvalidOperationException(
                $"NavigationFilter includes reference navigation '{navName}' on " +
                $"'{efType.ClrType.Name}', but IncludeReferences is false. " +
                $"Set IncludeReferences = true or remove the navigation from the filter.");
        }

        if (skipNavigation != null && !includeManyToMany)
        {
            throw new InvalidOperationException(
                $"NavigationFilter includes many-to-many navigation '{navName}' on " +
                $"'{efType.ClrType.Name}', but IncludeManyToMany is false. " +
                $"Set IncludeManyToMany = true or remove the navigation from the filter.");
        }
    }
}
