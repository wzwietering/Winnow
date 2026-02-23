namespace Winnow;

/// <summary>
/// Provides batch CRUD operations with failure isolation for entities with a known key type.
/// </summary>
/// <typeparam name="TEntity">The entity type to save.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
public interface IWinnower<TEntity, TKey>
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
    WinnowResult<TKey> Update(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="Update(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="options">Batch operation options.</param>
    WinnowResult<TKey> Update(IEnumerable<TEntity> entities, WinnowOptions options);

    /// <inheritdoc cref="Update(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> UpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Update(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to update.</param>
    /// <param name="options">Batch operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> UpdateAsync(IEnumerable<TEntity> entities, WinnowOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates entity graphs (parent + children) with failure isolation.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics.</returns>
    WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpdateGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="options">Graph batch operation options.</param>
    WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities, GraphOptions options);

    /// <inheritdoc cref="UpdateGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> UpdateGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The parent entities with their navigation properties loaded.</param>
    /// <param name="options">Graph batch operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> UpdateGraphAsync(IEnumerable<TEntity> entities, GraphOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Insert entities individually with failure isolation.
    /// </summary>
    InsertResult<TKey> Insert(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="Insert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="options">Insert operation options.</param>
    InsertResult<TKey> Insert(IEnumerable<TEntity> entities, InsertOptions options);

    /// <inheritdoc cref="Insert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<InsertResult<TKey>> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Insert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="options">Insert operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<InsertResult<TKey>> InsertAsync(IEnumerable<TEntity> entities, InsertOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert entity graphs (parent + children) with failure isolation.
    /// </summary>
    InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="InsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to insert.</param>
    /// <param name="options">Insert graph operation options.</param>
    InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities, InsertGraphOptions options);

    /// <inheritdoc cref="InsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to insert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<InsertResult<TKey>> InsertGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="InsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to insert.</param>
    /// <param name="options">Insert graph operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<InsertResult<TKey>> InsertGraphAsync(IEnumerable<TEntity> entities, InsertGraphOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Delete entities individually with failure isolation.
    /// </summary>
    WinnowResult<TKey> Delete(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="Delete(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to delete.</param>
    /// <param name="options">Delete operation options.</param>
    WinnowResult<TKey> Delete(IEnumerable<TEntity> entities, DeleteOptions options);

    /// <inheritdoc cref="Delete(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> DeleteAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Delete(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to delete.</param>
    /// <param name="options">Delete operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> DeleteAsync(IEnumerable<TEntity> entities, DeleteOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete entity graphs (parent + children) with failure isolation.
    /// </summary>
    WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="DeleteGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to delete.</param>
    /// <param name="options">Delete graph operation options.</param>
    WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities, DeleteGraphOptions options);

    /// <inheritdoc cref="DeleteGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> DeleteGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="DeleteGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The entity graphs to delete.</param>
    /// <param name="options">Delete graph operation options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<WinnowResult<TKey>> DeleteGraphAsync(IEnumerable<TEntity> entities, DeleteGraphOptions options, CancellationToken cancellationToken = default);

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
    /// between these steps, the INSERT will fail. Set <see cref="UpsertOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    ///
    /// <para><strong>Key Detection:</strong></para>
    /// <list type="bullet">
    /// <item>int/long: 0 → INSERT, other → UPDATE</item>
    /// <item>Guid: Empty → INSERT, other → UPDATE</item>
    /// <item>string: null/empty → INSERT, other → UPDATE</item>
    /// </list>
    /// </remarks>
    UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="options">Upsert configuration options.</param>
    UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities, UpsertOptions options);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation. Checked before each entity.</param>
    Task<UpsertResult<TKey>> UpsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="options">Upsert configuration options.</param>
    /// <param name="cancellationToken">Token to cancel the operation. Checked before each entity.</param>
    Task<UpsertResult<TKey>> UpsertAsync(IEnumerable<TEntity> entities, UpsertOptions options, CancellationToken cancellationToken = default);

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
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertGraphOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    ///
    /// <para><strong>Orphan Handling:</strong> For entities being updated, children removed from
    /// collections are handled according to <see cref="UpsertGraphOptions.OrphanedChildBehavior"/>.</para>
    /// </remarks>
    UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="options">Graph upsert configuration options.</param>
    UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities, UpsertGraphOptions options);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<UpsertResult<TKey>> UpsertGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    /// <param name="entities">The root entities of the graphs to upsert.</param>
    /// <param name="options">Graph upsert configuration options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<UpsertResult<TKey>> UpsertGraphAsync(IEnumerable<TEntity> entities, UpsertGraphOptions options, CancellationToken cancellationToken = default);
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
/// prefer <see cref="IWinnower{TEntity, TKey}"/> for better type safety.
/// </para>
/// <para>
/// <strong>Example (simple key):</strong>
/// <code>
/// var saver = new Winnower&lt;Product&gt;(context);
/// var result = saver.Insert(products);
/// int id = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// </code>
/// </para>
/// <para>
/// <strong>Example (composite key):</strong>
/// <code>
/// var saver = new Winnower&lt;OrderLine&gt;(context);
/// var result = saver.Insert(orderLines);
/// int orderId = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// int lineNum = result.InsertedIds[0].GetValue&lt;int&gt;(1);
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to save</typeparam>
public interface IWinnower<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Returns true if the entity has a composite primary key (more than one column).
    /// </summary>
    bool IsCompositeKey { get; }

    // === UPDATE OPERATIONS ===

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Update(IEnumerable{TEntity})"/>
    WinnowResult<CompositeKey> Update(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Update(IEnumerable{TEntity}, WinnowOptions)"/>
    WinnowResult<CompositeKey> Update(IEnumerable<TEntity> entities, WinnowOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> UpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateAsync(IEnumerable{TEntity}, WinnowOptions, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> UpdateAsync(IEnumerable<TEntity> entities, WinnowOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateGraph(IEnumerable{TEntity})"/>
    WinnowResult<CompositeKey> UpdateGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateGraph(IEnumerable{TEntity}, GraphOptions)"/>
    WinnowResult<CompositeKey> UpdateGraph(IEnumerable<TEntity> entities, GraphOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateGraphAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> UpdateGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.UpdateGraphAsync(IEnumerable{TEntity}, GraphOptions, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> UpdateGraphAsync(IEnumerable<TEntity> entities, GraphOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Insert(IEnumerable{TEntity})"/>
    InsertResult<CompositeKey> Insert(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Insert(IEnumerable{TEntity}, InsertOptions)"/>
    InsertResult<CompositeKey> Insert(IEnumerable<TEntity> entities, InsertOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<InsertResult<CompositeKey>> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertAsync(IEnumerable{TEntity}, InsertOptions, CancellationToken)"/>
    Task<InsertResult<CompositeKey>> InsertAsync(IEnumerable<TEntity> entities, InsertOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertGraph(IEnumerable{TEntity})"/>
    InsertResult<CompositeKey> InsertGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertGraph(IEnumerable{TEntity}, InsertGraphOptions)"/>
    InsertResult<CompositeKey> InsertGraph(IEnumerable<TEntity> entities, InsertGraphOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertGraphAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<InsertResult<CompositeKey>> InsertGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.InsertGraphAsync(IEnumerable{TEntity}, InsertGraphOptions, CancellationToken)"/>
    Task<InsertResult<CompositeKey>> InsertGraphAsync(IEnumerable<TEntity> entities, InsertGraphOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Delete(IEnumerable{TEntity})"/>
    WinnowResult<CompositeKey> Delete(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.Delete(IEnumerable{TEntity}, DeleteOptions)"/>
    WinnowResult<CompositeKey> Delete(IEnumerable<TEntity> entities, DeleteOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> DeleteAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteAsync(IEnumerable{TEntity}, DeleteOptions, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> DeleteAsync(IEnumerable<TEntity> entities, DeleteOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteGraph(IEnumerable{TEntity})"/>
    WinnowResult<CompositeKey> DeleteGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteGraph(IEnumerable{TEntity}, DeleteGraphOptions)"/>
    WinnowResult<CompositeKey> DeleteGraph(IEnumerable<TEntity> entities, DeleteGraphOptions options);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteGraphAsync(IEnumerable{TEntity}, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> DeleteGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IWinnower{TEntity, TKey}.DeleteGraphAsync(IEnumerable{TEntity}, DeleteGraphOptions, CancellationToken)"/>
    Task<WinnowResult<CompositeKey>> DeleteGraphAsync(IEnumerable<TEntity> entities, DeleteGraphOptions options, CancellationToken cancellationToken = default);

    // === UPSERT OPERATIONS ===

    /// <summary>
    /// Upserts entities individually. Entities with default keys are inserted; others are updated.
    /// </summary>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> Uses conditional INSERT/UPDATE based on key detection.</para>
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    /// </remarks>
    UpsertResult<CompositeKey> Upsert(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    UpsertResult<CompositeKey> Upsert(IEnumerable<TEntity> entities, UpsertOptions options);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    Task<UpsertResult<CompositeKey>> UpsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Upsert(IEnumerable{TEntity})"/>
    Task<UpsertResult<CompositeKey>> UpsertAsync(IEnumerable<TEntity> entities, UpsertOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts entity graphs (parent + children). Each entity is routed to INSERT or UPDATE based on its key.
    /// </summary>
    /// <remarks>
    /// <para><strong>NOT a database MERGE:</strong> Uses conditional INSERT/UPDATE based on key detection.</para>
    /// <para><strong>Race Condition:</strong> Set <see cref="UpsertGraphOptions.DuplicateKeyStrategy"/>
    /// to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> for automatic retry handling.</para>
    /// </remarks>
    UpsertResult<CompositeKey> UpsertGraph(IEnumerable<TEntity> entities);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    UpsertResult<CompositeKey> UpsertGraph(IEnumerable<TEntity> entities, UpsertGraphOptions options);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    Task<UpsertResult<CompositeKey>> UpsertGraphAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsertGraph(IEnumerable{TEntity})"/>
    Task<UpsertResult<CompositeKey>> UpsertGraphAsync(IEnumerable<TEntity> entities, UpsertGraphOptions options, CancellationToken cancellationToken = default);
}
