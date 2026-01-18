using EfCoreUtils.MixedKey;
using EfCoreUtils.Operations.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

internal class MixedKeyOneByOneGraphUpdateStrategy<TEntity> : IMixedKeyBatchGraphUpdateStrategy<TEntity>
    where TEntity : class
{
    public MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        GraphBatchOptions options)
    {
        var operation = new MixedKeyUpdateGraphOperation<TEntity>(options);
        var strategy = new MixedKeyGenericOneByOneStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
