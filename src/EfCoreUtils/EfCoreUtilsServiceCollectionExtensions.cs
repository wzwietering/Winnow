using EfCoreUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering EfCoreUtils services in the DI container.
/// </summary>
public static class EfCoreUtilsServiceCollectionExtensions
{
    /// <summary>
    /// Registers EfCoreUtils batch savers for the specified DbContext type.
    /// Registers <see cref="IBatchSaver{TEntity,TKey}"/> and <see cref="IBatchSaver{TEntity}"/> as scoped services.
    /// </summary>
    public static IServiceCollection AddEfCoreUtils<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped(typeof(IBatchSaver<,>), typeof(BatchSaver<,>));
        services.TryAddScoped(typeof(IBatchSaver<>), typeof(BatchSaver<>));

        return services;
    }
}
