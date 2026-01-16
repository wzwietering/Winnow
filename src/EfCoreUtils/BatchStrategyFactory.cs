using EfCoreUtils.Strategies;

namespace EfCoreUtils;

internal static class BatchStrategyFactory
{
    internal static IBatchUpdateStrategy<TEntity, TKey> CreateStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpdateStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchGraphUpdateStrategy<TEntity, TKey> CreateGraphStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneGraphUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerGraphUpdateStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchInsertStrategy<TEntity, TKey> CreateInsertStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchInsertGraphStrategy<TEntity, TKey> CreateInsertGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertGraphStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchDeleteStrategy<TEntity, TKey> CreateDeleteStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchDeleteGraphStrategy<TEntity, TKey> CreateDeleteGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteGraphStrategy<TEntity, TKey>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }
}
