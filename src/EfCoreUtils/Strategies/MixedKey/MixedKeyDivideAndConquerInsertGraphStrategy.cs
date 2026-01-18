using EfCoreUtils.MixedKey;
using EfCoreUtils.Operations.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

internal class MixedKeyDivideAndConquerInsertGraphStrategy<TEntity> : IMixedKeyBatchInsertGraphStrategy<TEntity>
    where TEntity : class
{
    public MixedKeyInsertBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options)
    {
        var operation = new MixedKeyInsertGraphOperation<TEntity>(options);
        var strategy = new MixedKeyGenericDivideAndConquerStrategy<TEntity>();
        return strategy.ExecuteInsert(entities, context, operation);
    }
}
