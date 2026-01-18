using EfCoreUtils.Strategies;

namespace EfCoreUtils;

internal static class BatchStrategyFactory
{
    private static ArgumentException UnknownStrategyException(BatchStrategy strategy) =>
        new($"Unknown batch strategy '{strategy}'. Valid values are: {string.Join(", ", Enum.GetNames<BatchStrategy>())}");

    internal static IBatchUpdateStrategy<TEntity, TKey> CreateStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpdateStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IBatchGraphUpdateStrategy<TEntity, TKey> CreateGraphStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneGraphUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerGraphUpdateStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IBatchInsertStrategy<TEntity, TKey> CreateInsertStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IBatchInsertGraphStrategy<TEntity, TKey> CreateInsertGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertGraphStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IBatchDeleteStrategy<TEntity, TKey> CreateDeleteStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IBatchDeleteGraphStrategy<TEntity, TKey> CreateDeleteGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteGraphStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };
}
