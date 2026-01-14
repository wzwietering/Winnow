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
}
