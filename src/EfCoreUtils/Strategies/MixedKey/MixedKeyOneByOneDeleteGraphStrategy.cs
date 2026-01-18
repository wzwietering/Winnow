using EfCoreUtils.MixedKey;
using EfCoreUtils.Operations.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

internal class MixedKeyOneByOneDeleteGraphStrategy<TEntity> : IMixedKeyBatchDeleteGraphStrategy<TEntity>
    where TEntity : class
{
    public MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        DeleteGraphBatchOptions options)
    {
        var operation = new MixedKeyDeleteGraphOperation<TEntity>(options);
        var strategy = new MixedKeyGenericOneByOneStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
