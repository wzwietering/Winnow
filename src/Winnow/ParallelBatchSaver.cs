using Winnow.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow;

/// <summary>
/// Batch saver that executes operations in parallel across multiple DbContext instances.
/// Each partition gets its own DbContext from the factory, enabling true parallel database operations.
/// </summary>
/// <remarks>
/// <para><strong>When to use ParallelBatchSaver:</strong></para>
/// <list type="bullet">
/// <item>Processing large batches (100+ entities) where database I/O is the bottleneck</item>
/// <item>High-latency database connections where parallel I/O provides significant speedup</item>
/// <item>Sufficient database connection pool capacity for concurrent operations</item>
/// </list>
/// <para><strong>When to use BatchSaver instead:</strong></para>
/// <list type="bullet">
/// <item>Small batches (under 100 entities) where parallelism overhead outweighs benefits</item>
/// <item>Operations requiring same-context change tracking (e.g. orphan detection)</item>
/// <item>Limited connection pool capacity</item>
/// <item>Operations that must be fully atomic (all-or-nothing)</item>
/// </list>
/// <para><strong>NOT atomic across partitions:</strong> Each partition commits independently.
/// If one partition fails, others that already committed will NOT be rolled back.
/// For atomic operations, use <see cref="BatchSaver{TEntity, TKey}"/> instead.</para>
/// <para><strong>Context factory requirement:</strong> The factory must return a new DbContext instance
/// on each call. Reusing instances across partitions causes concurrency issues.</para>
/// <para><strong>Sync methods:</strong> Run sequentially on a single context (no parallelism).
/// Only async methods benefit from parallel execution.</para>
/// </remarks>
public class ParallelBatchSaver<TEntity, TKey>
    : IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly Func<DbContext> _contextFactory;
    private readonly ILogger? _logger;

    /// <summary>
    /// Maximum number of parallel partitions for async operations.
    /// Sync operations always use a single partition regardless of this value.
    /// </summary>
    /// <remarks>
    /// <para>Set to 1 to disable parallel execution while still using the factory-based context lifecycle.</para>
    /// <para>Default: 4. Tune based on database connection capacity and batch size.</para>
    /// </remarks>
    public int MaxDegreeOfParallelism { get; }

    /// <summary>
    /// Creates a ParallelBatchSaver with a context factory for parallel database operations.
    /// </summary>
    /// <param name="contextFactory">Factory that creates a new DbContext on each call.</param>
    /// <param name="maxDegreeOfParallelism">Maximum parallel partitions (default: 4, minimum: 1).</param>
    /// <param name="logger">Optional logger for operation diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxDegreeOfParallelism is less than 1.</exception>
    /// <exception cref="ArgumentException">Thrown when factory returns the same instance twice.</exception>
    public ParallelBatchSaver(
        Func<DbContext> contextFactory,
        int maxDegreeOfParallelism = 4,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        if (maxDegreeOfParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Must be at least 1.");

        _contextFactory = contextFactory;
        _logger = logger;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;

        if (maxDegreeOfParallelism > 1)
            ValidateFactoryCreatesUniqueInstances(contextFactory);
    }

    // === UPDATE OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities) =>
        UpdateBatch(entities, new BatchOptions());

    /// <inheritdoc />
    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options) =>
        ExecuteSync(saver => saver.UpdateBatch(entities, options));

    /// <inheritdoc />
    public Task<BatchResult<TKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateBatchAsync(entities, new BatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<BatchResult<TKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, cancellationToken);

    // === UPDATE GRAPH OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities) =>
        UpdateGraphBatch(entities, new GraphBatchOptions());

    /// <inheritdoc />
    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options) =>
        ExecuteSync(saver => saver.UpdateGraphBatch(entities, options));

    /// <inheritdoc />
    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateGraphBatchAsync(entities, new GraphBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, cancellationToken);

    // === INSERT OPERATIONS ===

    /// <inheritdoc />
    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities) =>
        InsertBatch(entities, new InsertBatchOptions());

    /// <inheritdoc />
    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options) =>
        ExecuteSync(saver => saver.InsertBatch(entities, options));

    /// <inheritdoc />
    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertBatchAsync(entities, new InsertBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmptyInsert<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteInsertAsync(list, exec, ct), options.Retry, cancellationToken);

    // === INSERT GRAPH OPERATIONS ===

    /// <inheritdoc />
    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities) =>
        InsertGraphBatch(entities, new InsertGraphBatchOptions());

    /// <inheritdoc />
    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options) =>
        ExecuteSync(saver => saver.InsertGraphBatch(entities, options));

    /// <inheritdoc />
    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertGraphBatchAsync(entities, new InsertGraphBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmptyInsert<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteInsertAsync(list, exec, ct), options.Retry, cancellationToken);

    // === DELETE OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities) =>
        DeleteBatch(entities, new DeleteBatchOptions());

    /// <inheritdoc />
    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options) =>
        ExecuteSync(saver => saver.DeleteBatch(entities, options));

    /// <inheritdoc />
    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(entities, new DeleteBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, cancellationToken);

    // === DELETE GRAPH OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities) =>
        DeleteGraphBatch(entities, new DeleteGraphBatchOptions());

    /// <inheritdoc />
    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options) =>
        ExecuteSync(saver => saver.DeleteGraphBatch(entities, options));

    /// <inheritdoc />
    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteGraphBatchAsync(entities, new DeleteGraphBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, cancellationToken);

    // === UPSERT OPERATIONS ===

    /// <inheritdoc />
    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities) =>
        UpsertBatch(entities, new UpsertBatchOptions());

    /// <inheritdoc />
    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options) =>
        ExecuteSync(saver => saver.UpsertBatch(entities, options));

    /// <inheritdoc />
    public Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertBatchAsync(entities, new UpsertBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmptyUpsert<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteUpsertAsync(list, exec, ct), options.Retry, cancellationToken);

    // === UPSERT GRAPH OPERATIONS ===

    /// <inheritdoc />
    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities) =>
        UpsertGraphBatch(entities, new UpsertGraphBatchOptions());

    /// <inheritdoc />
    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options) =>
        ExecuteSync(saver => saver.UpsertGraphBatch(entities, options));

    /// <inheritdoc />
    public Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertGraphBatchAsync(entities, new UpsertGraphBatchOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => BatchResultFactory.CreateEmptyUpsert<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteUpsertAsync(list, exec, ct), options.Retry, cancellationToken);

    // === PRIVATE HELPERS ===

    private TResult ExecuteSync<TResult>(Func<BatchSaver<TEntity, TKey>, TResult> operation)
    {
        using var context = _contextFactory();
        var saver = new BatchSaver<TEntity, TKey>(context, _logger);
        return operation(saver);
    }

    private async Task<TResult> ExecuteSequentialAsync<TResult>(
        List<TEntity> entityList,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        RetryOptions? retryOptions,
        CancellationToken cancellationToken)
    {
        var context = _contextFactory();
        try
        {
            var strategyContext = new BatchStrategyContext<TEntity, TKey>(context)
                { Logger = _logger, RetryOptions = retryOptions };
            return await execute(entityList, strategyContext, cancellationToken);
        }
        finally
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<TResult> RunAsync<TResult>(
        IEnumerable<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<TResult> createEmpty,
        Func<ParallelExecutionOrchestrator<TEntity, TKey>, List<TEntity>,
            Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>>,
            CancellationToken, Task<TResult>> orchestrate,
        RetryOptions? retryOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return createEmpty();

        if (MaxDegreeOfParallelism <= 1)
            return await ExecuteSequentialAsync(entityList, execute, retryOptions, cancellationToken);

        using var orchestrator = new ParallelExecutionOrchestrator<TEntity, TKey>(
            _contextFactory, MaxDegreeOfParallelism, _logger, retryOptions);
        return await orchestrate(orchestrator, entityList, execute, cancellationToken);
    }

    private static void ValidateFactoryCreatesUniqueInstances(Func<DbContext> factory)
    {
        DbContext? first = null;
        DbContext? second = null;
        try
        {
            first = factory();
            second = factory();

            if (ReferenceEquals(first, second))
                throw new ArgumentException(
                    "The context factory must return a new DbContext instance on each call. " +
                    "The same instance was returned twice, which would cause concurrency issues.",
                    nameof(factory));
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }
    }
}
