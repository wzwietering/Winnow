namespace EfCoreUtils;

internal interface IBatchGraphUpdateStrategy<TEntity> where TEntity : class
{
    BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options);
}
