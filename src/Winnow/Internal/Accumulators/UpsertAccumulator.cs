namespace Winnow.Internal.Accumulators;

/// <summary>
/// Accumulates per-entity success and failure data for upsert operations.
/// Tracks insert vs update decisions per index. Allocation is gated by
/// <see cref="ResultDetail"/>.
/// </summary>
internal sealed class UpsertAccumulator<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly ResultDetail _detail;
    private readonly Dictionary<int, UpsertOperationType> _operationDecisions = [];
    private readonly List<UpsertedEntity<TKey>>? _insertedEntities;
    private readonly List<UpsertedEntity<TKey>>? _updatedEntities;
    private readonly List<TKey>? _insertedIds;
    private readonly List<TKey>? _updatedIds;
    private readonly List<UpsertFailure<TKey>>? _failures;
    private int _insertedCount;
    private int _updatedCount;
    private int _failureCount;

    internal UpsertAccumulator(ResultDetail detail)
    {
        _detail = detail;
        if (detail >= ResultDetail.Full)
        {
            _insertedEntities = [];
            _updatedEntities = [];
        }
        else if (detail >= ResultDetail.Minimal)
        {
            _insertedIds = [];
            _updatedIds = [];
        }
        if (detail >= ResultDetail.Minimal)
        {
            _failures = [];
        }
    }

    internal void RecordOperationDecision(int index, UpsertOperationType operation) =>
        _operationDecisions[index] = operation;

    internal UpsertOperationType GetOperationDecision(int index) =>
        _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);

    internal bool WasInsertAttempt(int index) =>
        GetOperationDecision(index) == UpsertOperationType.Insert;

    internal void RecordSuccess(TKey id, int index, object entity)
    {
        var operation = GetOperationDecision(index);
        TallySuccess(operation);
        AddSuccess(id, index, entity, operation);
    }

    internal void RecordSuccessAsUpdate(TKey id, int index, object entity)
    {
        _operationDecisions[index] = UpsertOperationType.Update;
        TallySuccess(UpsertOperationType.Update);
        AddSuccess(id, index, entity, UpsertOperationType.Update);
    }

    private void TallySuccess(UpsertOperationType operation)
    {
        if (operation == UpsertOperationType.Insert)
        {
            _insertedCount++;
        }
        else
        {
            _updatedCount++;
        }
    }

    private void AddSuccess(TKey id, int index, object entity, UpsertOperationType operation)
    {
        if (_detail >= ResultDetail.Full)
        {
            var entry = new UpsertedEntity<TKey>
            {
                Id = id,
                OriginalIndex = index,
                Entity = entity,
                Operation = operation
            };
            (operation == UpsertOperationType.Insert ? _insertedEntities! : _updatedEntities!).Add(entry);
        }
        else if (_detail >= ResultDetail.Minimal)
        {
            (operation == UpsertOperationType.Insert ? _insertedIds! : _updatedIds!).Add(id);
        }
    }

    internal void RecordFailure(
        int index,
        TKey? entityId,
        string errorMessage,
        FailureReason reason,
        Exception? exception,
        UpsertOperationType attemptedOperation)
    {
        _failureCount++;
        if (_detail < ResultDetail.Minimal)
        {
            return;
        }
        _failures!.Add(new UpsertFailure<TKey>
        {
            EntityIndex = index,
            EntityId = entityId,
            ErrorMessage = errorMessage,
            Reason = reason,
            Exception = _detail >= ResultDetail.Full ? exception : null,
            AttemptedOperation = attemptedOperation,
            IsDefaultKey = attemptedOperation == UpsertOperationType.Insert
        });
    }

    internal int SuccessCount => _insertedCount + _updatedCount;
    internal int FailureCount => _failureCount;

    internal UpsertResult<TKey> Build(bool wasCancelled, GraphResultAccumulator<TKey>? graph = null) => new()
    {
        ResultDetail = _detail,
        InsertedEntities = (IReadOnlyList<UpsertedEntity<TKey>>?)_insertedEntities ?? [],
        UpdatedEntities = (IReadOnlyList<UpsertedEntity<TKey>>?)_updatedEntities ?? [],
        InsertedIds = (IReadOnlyList<TKey>?)_insertedIds ?? [],
        UpdatedIds = (IReadOnlyList<TKey>?)_updatedIds ?? [],
        Failures = (IReadOnlyList<UpsertFailure<TKey>>?)_failures ?? [],
        SuccessCount = SuccessCount,
        FailureCount = _failureCount,
        InsertedCount = _insertedCount,
        UpdatedCount = _updatedCount,
        WasCancelled = wasCancelled,
        GraphHierarchy = graph?.Hierarchy,
        TraversalInfo = graph?.TraversalInfo
    };
}
