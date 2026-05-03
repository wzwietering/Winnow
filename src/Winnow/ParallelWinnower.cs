using Winnow.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow;

/// <summary>
/// Batch saver that executes operations in parallel across multiple DbContext instances.
/// Each partition gets its own DbContext from the factory, enabling true parallel database operations.
/// </summary>
/// <remarks>
/// <para><strong>When to use ParallelWinnower:</strong></para>
/// <list type="bullet">
/// <item>Processing large batches (100+ entities) where database I/O is the bottleneck</item>
/// <item>High-latency database connections where parallel I/O provides significant speedup</item>
/// <item>Sufficient database connection pool capacity for concurrent operations</item>
/// </list>
/// <para><strong>When to use Winnower instead:</strong></para>
/// <list type="bullet">
/// <item>Small batches (under 100 entities) where parallelism overhead outweighs benefits</item>
/// <item>Operations requiring same-context change tracking (e.g. orphan detection)</item>
/// <item>Limited connection pool capacity</item>
/// <item>Operations that must be fully atomic (all-or-nothing)</item>
/// </list>
/// <para><strong>NOT atomic across partitions:</strong> Each partition commits independently.
/// If one partition fails, others that already committed will NOT be rolled back.
/// For atomic operations, use <see cref="Winnower{TEntity, TKey}"/> instead.</para>
/// <para><strong>Context factory requirement:</strong> The factory must return a new DbContext instance
/// on each call. Reusing instances across partitions causes concurrency issues.</para>
/// <para><strong>Sync methods:</strong> Run sequentially on a single context (no parallelism).
/// Only async methods benefit from parallel execution.</para>
/// </remarks>
public class ParallelWinnower<TEntity, TKey>
    : IWinnower<TEntity, TKey>
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
    /// Creates a ParallelWinnower with a context factory for parallel database operations.
    /// </summary>
    /// <param name="contextFactory">Factory that creates a new DbContext on each call.</param>
    /// <param name="maxDegreeOfParallelism">Maximum parallel partitions (default: 4, minimum: 1).</param>
    /// <param name="logger">Optional logger for operation diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxDegreeOfParallelism is less than 1.</exception>
    /// <exception cref="ArgumentException">Thrown when factory returns the same instance twice.</exception>
    public ParallelWinnower(
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
    public WinnowResult<TKey> Update(IEnumerable<TEntity> entities) =>
        Update(entities, new WinnowOptions());

    /// <inheritdoc />
    public WinnowResult<TKey> Update(IEnumerable<TEntity> entities, WinnowOptions options) =>
        ExecuteSync(saver => saver.Update(entities, options));

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> UpdateAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateAsync(entities, new WinnowOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<WinnowResult<TKey>> UpdateAsync(
        IEnumerable<TEntity> entities, WinnowOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === UPDATE GRAPH OPERATIONS ===

    /// <inheritdoc />
    public WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities) =>
        UpdateGraph(entities, new GraphOptions());

    /// <inheritdoc />
    public WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities, GraphOptions options) =>
        ExecuteSync(saver => saver.UpdateGraph(entities, options));

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateGraphAsync(entities, new GraphOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<WinnowResult<TKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities, GraphOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === INSERT OPERATIONS ===

    /// <inheritdoc />
    public InsertResult<TKey> Insert(IEnumerable<TEntity> entities) =>
        Insert(entities, new InsertOptions());

    /// <inheritdoc />
    public InsertResult<TKey> Insert(IEnumerable<TEntity> entities, InsertOptions options) =>
        ExecuteSync(saver => saver.Insert(entities, options));

    /// <inheritdoc />
    public Task<InsertResult<TKey>> InsertAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertAsync(entities, new InsertOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<InsertResult<TKey>> InsertAsync(
        IEnumerable<TEntity> entities, InsertOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmptyInsert<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteInsertAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === INSERT GRAPH OPERATIONS ===

    /// <inheritdoc />
    public InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities) =>
        InsertGraph(entities, new InsertGraphOptions());

    /// <inheritdoc />
    public InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities, InsertGraphOptions options) =>
        ExecuteSync(saver => saver.InsertGraph(entities, options));

    /// <inheritdoc />
    public Task<InsertResult<TKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertGraphAsync(entities, new InsertGraphOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<InsertResult<TKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities, InsertGraphOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmptyInsert<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteInsertAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === DELETE OPERATIONS ===

    /// <inheritdoc />
    public WinnowResult<TKey> Delete(IEnumerable<TEntity> entities) =>
        Delete(entities, new DeleteOptions());

    /// <inheritdoc />
    public WinnowResult<TKey> Delete(IEnumerable<TEntity> entities, DeleteOptions options) =>
        ExecuteSync(saver => saver.Delete(entities, options));

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> DeleteAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteAsync(entities, new DeleteOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<WinnowResult<TKey>> DeleteAsync(
        IEnumerable<TEntity> entities, DeleteOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === DELETE GRAPH OPERATIONS ===

    /// <inheritdoc />
    public WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities) =>
        DeleteGraph(entities, new DeleteGraphOptions());

    /// <inheritdoc />
    public WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities, DeleteGraphOptions options) =>
        ExecuteSync(saver => saver.DeleteGraph(entities, options));

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteGraphAsync(entities, new DeleteGraphOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<WinnowResult<TKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities, DeleteGraphOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteBatchAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === UPSERT OPERATIONS ===

    /// <inheritdoc />
    public UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities) =>
        Upsert(entities, new UpsertOptions());

    /// <inheritdoc />
    public UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities, UpsertOptions options) =>
        ExecuteSync(saver => saver.Upsert(entities, options));

    /// <inheritdoc />
    public Task<UpsertResult<TKey>> UpsertAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertAsync(entities, new UpsertOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<UpsertResult<TKey>> UpsertAsync(
        IEnumerable<TEntity> entities, UpsertOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmptyUpsert<TKey>(TimeSpan.Zero, false),
        (o, list, exec, ct) => o.ExecuteUpsertAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === UPSERT GRAPH OPERATIONS ===

    /// <inheritdoc />
    public UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities) =>
        UpsertGraph(entities, new UpsertGraphOptions());

    /// <inheritdoc />
    public UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities, UpsertGraphOptions options) =>
        ExecuteSync(saver => saver.UpsertGraph(entities, options));

    /// <inheritdoc />
    public Task<UpsertResult<TKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertGraphAsync(entities, new UpsertGraphOptions(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not atomic across partitions. If one partition fails, others that already committed will NOT be rolled back.</remarks>
    public Task<UpsertResult<TKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities, UpsertGraphOptions options, CancellationToken cancellationToken = default) =>
        RunAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = StrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, () => ResultFactory.CreateEmptyUpsert<TKey>(TimeSpan.Zero, true),
        (o, list, exec, ct) => o.ExecuteUpsertAsync(list, exec, ct), options.Retry, options.ResultDetail, cancellationToken);

    // === PRIVATE HELPERS ===

    private TResult ExecuteSync<TResult>(Func<Winnower<TEntity, TKey>, TResult> operation)
    {
        using var context = _contextFactory();
        var saver = new Winnower<TEntity, TKey>(context, _logger);
        return operation(saver);
    }

    private async Task<TResult> ExecuteSequentialAsync<TResult>(
        List<TEntity> entityList,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        RetryOptions? retryOptions,
        CancellationToken cancellationToken)
    {
        var context = _contextFactory();
        try
        {
            var strategyContext = new StrategyContext<TEntity, TKey>(context)
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
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<TResult> createEmpty,
        Func<ParallelExecutionOrchestrator<TEntity, TKey>, List<TEntity>,
            Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>>,
            CancellationToken, Task<TResult>> orchestrate,
        RetryOptions? retryOptions,
        ResultDetail resultDetail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return createEmpty();

        if (MaxDegreeOfParallelism <= 1)
            return await ExecuteSequentialAsync(entityList, execute, retryOptions, cancellationToken);

        using var orchestrator = new ParallelExecutionOrchestrator<TEntity, TKey>(
            _contextFactory, MaxDegreeOfParallelism, _logger, retryOptions, resultDetail);
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
