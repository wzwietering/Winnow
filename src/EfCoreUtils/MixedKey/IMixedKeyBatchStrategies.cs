namespace EfCoreUtils.MixedKey;

internal interface IMixedKeyBatchGraphUpdateStrategy<TEntity>
    where TEntity : class
{
    MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        GraphBatchOptions options);
}

internal interface IMixedKeyBatchInsertGraphStrategy<TEntity>
    where TEntity : class
{
    MixedKeyInsertBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options);
}

internal interface IMixedKeyBatchDeleteGraphStrategy<TEntity>
    where TEntity : class
{
    MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        DeleteGraphBatchOptions options);
}
