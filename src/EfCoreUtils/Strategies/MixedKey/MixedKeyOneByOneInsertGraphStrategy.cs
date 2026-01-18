using EfCoreUtils.MixedKey;
using EfCoreUtils.Operations.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

internal class MixedKeyOneByOneInsertGraphStrategy<TEntity> : IMixedKeyBatchInsertGraphStrategy<TEntity>
    where TEntity : class
{
    public MixedKeyInsertBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options)
    {
        var operation = new MixedKeyInsertGraphOperation<TEntity>(options);
        var strategy = new MixedKeyGenericOneByOneStrategy<TEntity>();
        return strategy.ExecuteInsert(entities, context, operation);
    }
}
