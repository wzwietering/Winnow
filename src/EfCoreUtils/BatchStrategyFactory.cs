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
}
