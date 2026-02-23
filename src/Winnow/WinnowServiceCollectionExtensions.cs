using Winnow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Winnow services in the DI container.
/// </summary>
public static class WinnowServiceCollectionExtensions
{
    /// <summary>
    /// Registers Winnow batch savers for the specified DbContext type.
    /// Registers <see cref="IWinnower{TEntity,TKey}"/> and <see cref="IWinnower{TEntity}"/> as scoped services.
    /// </summary>
    /// <remarks>
    /// <para><strong>Single context only:</strong> This method registers <see cref="DbContext"/> as a scoped
    /// alias to <typeparamref name="TContext"/>. Calling this method for multiple DbContext types is not
    /// supported — only the first registration takes effect. Subsequent calls are silently ignored.</para>
    /// <para><strong>ParallelWinnower:</strong> <see cref="ParallelWinnower{TEntity,TKey}"/> requires
    /// a context factory and is not registered through DI. Construct it manually.</para>
    /// </remarks>
    public static IServiceCollection AddWinnow<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped(typeof(IWinnower<,>), typeof(Winnower<,>));
        services.TryAddScoped(typeof(IWinnower<>), typeof(Winnower<>));

        return services;
    }
}
