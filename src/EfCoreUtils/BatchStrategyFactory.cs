using EfCoreUtils.Strategies;

namespace EfCoreUtils;

internal static class BatchStrategyFactory
{
    internal static IBatchUpdateStrategy<TEntity> CreateStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneUpdateStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerUpdateStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchGraphUpdateStrategy<TEntity> CreateGraphStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneGraphUpdateStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerGraphUpdateStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchInsertStrategy<TEntity> CreateInsertStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchInsertGraphStrategy<TEntity> CreateInsertGraphStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneInsertGraphStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerInsertGraphStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchDeleteStrategy<TEntity> CreateDeleteStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    internal static IBatchDeleteGraphStrategy<TEntity> CreateDeleteGraphStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class
    {
        return strategy switch
        {
            BatchStrategy.OneByOne => new OneByOneDeleteGraphStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new DivideAndConquerDeleteGraphStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }
}
