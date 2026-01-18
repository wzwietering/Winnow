using EfCoreUtils.Strategies.MixedKey;

namespace EfCoreUtils.MixedKey;

internal static class MixedKeyBatchStrategyFactory
{
    internal static IMixedKeyBatchGraphUpdateStrategy<TEntity> CreateGraphStrategy<TEntity>(BatchStrategy strategy)
        where TEntity : class => strategy switch
        {
            BatchStrategy.OneByOne => new MixedKeyOneByOneGraphUpdateStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new MixedKeyDivideAndConquerGraphUpdateStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };

    internal static IMixedKeyBatchInsertGraphStrategy<TEntity> CreateInsertGraphStrategy<TEntity>(
        BatchStrategy strategy)
        where TEntity : class => strategy switch
        {
            BatchStrategy.OneByOne => new MixedKeyOneByOneInsertGraphStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new MixedKeyDivideAndConquerInsertGraphStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };

    internal static IMixedKeyBatchDeleteGraphStrategy<TEntity> CreateDeleteGraphStrategy<TEntity>(
        BatchStrategy strategy)
        where TEntity : class => strategy switch
        {
            BatchStrategy.OneByOne => new MixedKeyOneByOneDeleteGraphStrategy<TEntity>(),
            BatchStrategy.DivideAndConquer => new MixedKeyDivideAndConquerDeleteGraphStrategy<TEntity>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
}
