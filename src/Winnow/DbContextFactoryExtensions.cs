using Microsoft.EntityFrameworkCore;

namespace Winnow;

/// <summary>
/// Extension methods on <see cref="IDbContextFactory{TContext}"/> for creating parallel batch savers.
/// </summary>
public static class DbContextFactoryExtensions
{
    /// <summary>
    /// Creates a <see cref="ParallelWinnower{TEntity, TKey}"/> using this factory to produce DbContext instances.
    /// </summary>
    public static ParallelWinnower<TEntity, TKey> CreateParallelWinnower<TEntity, TKey, TContext>(
        this IDbContextFactory<TContext> factory, int maxDegreeOfParallelism = 4)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
        where TContext : DbContext =>
        new(factory.CreateDbContext, maxDegreeOfParallelism);

    /// <summary>
    /// Creates a <see cref="ParallelWinnower{TEntity}"/> that auto-detects the key type at runtime.
    /// </summary>
    public static ParallelWinnower<TEntity> CreateParallelWinnower<TEntity, TContext>(
        this IDbContextFactory<TContext> factory, int maxDegreeOfParallelism = 4)
        where TEntity : class
        where TContext : DbContext =>
        new(factory.CreateDbContext, maxDegreeOfParallelism);
}
