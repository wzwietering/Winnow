namespace EfCoreUtils;

public interface IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    // === UPDATE OPERATIONS ===

    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult<TKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Insert entities individually with failure isolation.
    /// </summary>
    InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options);
    Task<InsertBatchResult<TKey>> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<TKey>> InsertBatchAsync(IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert entity graphs (parent + children) with failure isolation.
    /// </summary>
    InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options);
    Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Delete entities individually with failure isolation.
    /// </summary>
    BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options);
    Task<BatchResult<TKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete entity graphs (parent + children) with failure isolation.
    /// </summary>
    BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options);
    Task<BatchResult<TKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === UPSERT OPERATIONS ===

    /// <summary>
    /// Upserts a batch of entities: inserts if key is default, updates if key is non-default.
    /// </summary>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> This is NOT atomic MERGE/INSERT ON CONFLICT.
    /// It performs conditional INSERT or UPDATE based on key detection.</para>
    /// </remarks>
    UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities);
    UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options);
    Task<UpsertBatchResult<TKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<UpsertBatchResult<TKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts entity graphs (parent + children). Each entity is routed to INSERT or UPDATE based on its key.
    /// </summary>
    UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities);
    UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options);
    Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch saver interface that automatically detects entity key type at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides batch operations for entities where the key type is determined
/// at runtime. All results return <see cref="CompositeKey"/> to maintain a consistent API surface.
/// </para>
/// <para>
/// <strong>When to use:</strong> Use this when working with entities that have composite keys,
/// or when the key type isn't known at compile time.
/// </para>
/// <para>
/// <strong>When NOT to use:</strong> For entities with known simple keys (int, long, Guid),
/// prefer <see cref="IBatchSaver{TEntity, TKey}"/> for better type safety.
/// </para>
/// <para>
/// <strong>Example (simple key):</strong>
/// <code>
/// var saver = new BatchSaver&lt;Product&gt;(context);
/// var result = saver.InsertBatch(products);
/// int id = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// </code>
/// </para>
/// <para>
/// <strong>Example (composite key):</strong>
/// <code>
/// var saver = new BatchSaver&lt;OrderLine&gt;(context);
/// var result = saver.InsertBatch(orderLines);
/// int orderId = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// int lineNum = result.InsertedIds[0].GetValue&lt;int&gt;(1);
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to save</typeparam>
public interface IBatchSaver<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Returns true if the entity has a composite primary key (more than one column).
    /// </summary>
    bool IsCompositeKey { get; }

    // === UPDATE OPERATIONS ===

    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options);
    Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default);

    InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options);
    Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options);
    Task<BatchResult<CompositeKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options);
    Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === UPSERT OPERATIONS ===

    UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities);
    UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options);
    Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default);

    UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities);
    UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options);
    Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default);
}

public class BatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), validates that navigation properties are not modified.
    /// Set to false to allow navigation properties to be loaded but ignored.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;
}

public enum BatchStrategy
{
    OneByOne,
    DivideAndConquer
}
