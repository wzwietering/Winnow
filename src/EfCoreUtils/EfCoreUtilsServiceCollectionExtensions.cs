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
    /// <remarks>
    /// <para><strong>Single context only:</strong> This method registers <see cref="DbContext"/> as a scoped
    /// alias to <typeparamref name="TContext"/>. Calling this method for multiple DbContext types is not
    /// supported — only the first registration takes effect. Subsequent calls are silently ignored.</para>
    /// <para><strong>ParallelBatchSaver:</strong> <see cref="ParallelBatchSaver{TEntity,TKey}"/> requires
    /// a context factory and is not registered through DI. Construct it manually.</para>
    /// </remarks>
    public static IServiceCollection AddEfCoreUtils<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped(typeof(IBatchSaver<,>), typeof(BatchSaver<,>));
        services.TryAddScoped(typeof(IBatchSaver<>), typeof(BatchSaver<>));

        return services;
    }
}
