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
    /// Registers EfCoreUtils batch savers with default options.
    /// </summary>
    public static IServiceCollection AddEfCoreUtils<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddEfCoreUtils<TContext>(null);

    /// <summary>
    /// Registers EfCoreUtils batch savers with optional configuration.
    /// </summary>
    public static IServiceCollection AddEfCoreUtils<TContext>(
        this IServiceCollection services,
        Action<EfCoreUtilsOptions>? configure)
        where TContext : DbContext
    {
        var options = new EfCoreUtilsOptions();
        configure?.Invoke(options);

        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped(typeof(IBatchSaver<,>), typeof(BatchSaver<,>));
        services.TryAddScoped(typeof(IBatchSaver<>), typeof(BatchSaver<>));

        return services;
    }
}
