using Winnow.Strategies;

namespace Winnow;

internal static class StrategyFactory
{
    private static ArgumentException UnknownStrategyException(BatchStrategy strategy) =>
        new($"Unknown batch strategy '{strategy}'. Valid values are: {string.Join(", ", Enum.GetNames<BatchStrategy>())}");

    internal static IUpdateStrategy<TEntity, TKey> CreateStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpdateStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IGraphUpdateStrategy<TEntity, TKey> CreateGraphStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneGraphUpdateStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerGraphUpdateStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IInsertStrategy<TEntity, TKey> CreateInsertStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IInsertGraphStrategy<TEntity, TKey> CreateInsertGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertGraphStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IDeleteStrategy<TEntity, TKey> CreateDeleteStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IDeleteGraphStrategy<TEntity, TKey> CreateDeleteGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteGraphStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IUpsertStrategy<TEntity, TKey> CreateUpsertStrategy<TEntity, TKey>(BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpsertStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpsertStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };

    internal static IUpsertGraphStrategy<TEntity, TKey> CreateUpsertGraphStrategy<TEntity, TKey>(
        BatchStrategy strategy)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey> => strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpsertGraphStrategy<TEntity, TKey>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpsertGraphStrategy<TEntity, TKey>(),
            _ => throw UnknownStrategyException(strategy)
        };
}
