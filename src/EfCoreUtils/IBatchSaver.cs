namespace EfCoreUtils;

public interface IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    // === UPDATE OPERATIONS ===

    /// <summary>
    /// Updates entities individually with failure isolation.
    /// Only properties of TEntity are updated; navigation properties are NOT modified.
    /// </summary>
    /// <param name="entities">The entities to update.</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics.</returns>
    /// <exception cref="InvalidOperationException">Thrown when navigation properties are modified and ValidateNavigationProperties is true.</exception>
    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpdateBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="options">Batch operation options.</param>
    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);

    /// <inheritdoc cref="UpdateBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="options">Batch operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates entity graphs (parent + children) with failure isolation.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics.</returns>
    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpdateGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="options">Graph batch operation options.</param>
    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);

    /// <inheritdoc cref="UpdateGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<BatchResult<TKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="options">Graph batch operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
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
    /// Upserts entities individually with failure isolation.
    /// Entities with default keys are inserted; entities with non-default keys are updated.
    /// </summary>
    /// <param name="entities">The entities to upsert.</param>
    /// <returns>Result containing inserted entities, updated entities, and failures.</returns>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> This is NOT atomic MERGE/INSERT ON CONFLICT.
    /// It performs conditional INSERT or UPDATE based on key detection.</para>
    ///
    /// <para><strong>Race Condition:</strong> There is a potential race condition between
    /// key detection and SaveChanges. If another process inserts a row with the same key
    /// between these steps, the INSERT will fail. Set <see cref="UpsertBatchOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    ///
    /// <para><strong>Key Detection:</strong></para>
    /// <list type="bullet">
    /// <item>int/long: 0 → INSERT, other → UPDATE</item>
    /// <item>Guid: Empty → INSERT, other → UPDATE</item>
    /// <item>string: null/empty → INSERT, other → UPDATE</item>
    /// </list>
    /// </remarks>
    UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="options">Upsert configuration options.</param>
    UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation. Checked before each entity.</param>
    Task<UpsertBatchResult<TKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="options">Upsert configuration options.</param>
    /// <param name="cancellationToken">Token to cancel the operation. Checked before each entity.</param>
    Task<UpsertBatchResult<TKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts entity graphs (parent + children) with failure isolation.
    /// Each entity in the graph is routed to INSERT or UPDATE based on its key.
    /// </summary>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <returns>Result containing inserted entities, updated entities, graph hierarchy, and failures.</returns>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> This is NOT atomic MERGE/INSERT ON CONFLICT.
    /// It performs conditional INSERT or UPDATE based on key detection for each entity in the graph.</para>
    ///
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertGraphBatchOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    ///
    /// <para><strong>Orphan Handling:</strong> For entities being updated, children removed from
    /// collections are handled according to <see cref="UpsertGraphBatchOptions.OrphanedChildBehavior"/>.</para>
    /// </remarks>
    UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="options">Graph upsert configuration options.</param>
    UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="options">Graph upsert configuration options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
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

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateBatch(IEnumerable{TEntity})"/>
    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateBatch(IEnumerable{TEntity}, BatchOptions)"/>
    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateBatchAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateBatchAsync(IEnumerable{TEntity}, BatchOptions, CancellationToken)"/>
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateGraphBatch(IEnumerable{TEntity})"/>
    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateGraphBatch(IEnumerable{TEntity}, GraphBatchOptions)"/>
    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateGraphBatchAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IBatchSaver{TEntity, TKey}.UpdateGraphBatchAsync(IEnumerable{TEntity}, GraphBatchOptions, CancellationToken)"/>
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

    /// <summary>
    /// Upserts entities individually. Entities with default keys are inserted; others are updated.
    /// </summary>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> Uses conditional INSERT/UPDATE based on key detection.</para>
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertBatchOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    /// </remarks>
    UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertBatch(IEnumerable{TEntity})"/>
    Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts entity graphs (parent + children). Each entity is routed to INSERT or UPDATE based on its key.
    /// </summary>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> Uses conditional INSERT/UPDATE based on key detection.</para>
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertGraphBatchOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    /// </remarks>
    UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertGraphBatch(IEnumerable{TEntity})"/>
    Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default);
}
