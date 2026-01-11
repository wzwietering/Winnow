namespace EfCoreUtils;

internal interface IBatchUpdateStrategy<TEntity> where TEntity : class
{
    BatchResult Execute(List<TEntity> entities, BatchStrategyContext<TEntity> context, BatchOptions options);
}
