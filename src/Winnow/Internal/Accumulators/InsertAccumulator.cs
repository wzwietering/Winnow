namespace Winnow.Internal.Accumulators;

/// <summary>
/// Accumulates per-entity success and failure data for insert operations.
/// Allocation cost is gated by <see cref="ResultDetail"/>: <see cref="ResultDetail.Full"/>
/// captures entity references, <see cref="ResultDetail.Minimal"/> captures only IDs and
/// failure metadata, <see cref="ResultDetail.None"/> captures only counts.
/// </summary>
internal sealed class InsertAccumulator<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly ResultDetail _detail;
    private readonly List<InsertedEntity<TKey>>? _insertedEntities;
    private readonly List<TKey>? _insertedIds;
    private readonly List<InsertFailure>? _failures;
    private int _successCount;
    private int _failureCount;

    internal InsertAccumulator(ResultDetail detail)
    {
        _detail = detail;
        if (detail >= ResultDetail.Full)
        {
            _insertedEntities = [];
        }
        else if (detail >= ResultDetail.Minimal)
        {
            _insertedIds = [];
        }
        if (detail >= ResultDetail.Minimal)
        {
            _failures = [];
        }
    }

    internal void RecordSuccess(TKey id, int index, object entity)
    {
        _successCount++;
        if (_detail >= ResultDetail.Full)
        {
            _insertedEntities!.Add(new InsertedEntity<TKey>
            {
                Id = id,
                OriginalIndex = index,
                Entity = entity
            });
        }
        else if (_detail >= ResultDetail.Minimal)
        {
            _insertedIds!.Add(id);
        }
    }

    internal void RecordFailure(int index, string errorMessage, FailureReason reason, Exception? exception)
    {
        _failureCount++;
        if (_detail < ResultDetail.Minimal)
        {
            return;
        }
        _failures!.Add(new InsertFailure
        {
            EntityIndex = index,
            ErrorMessage = errorMessage,
            Reason = reason,
            Exception = _detail >= ResultDetail.Full ? exception : null
        });
    }

    internal int SuccessCount => _successCount;
    internal int FailureCount => _failureCount;

    internal InsertResult<TKey> Build(bool wasCancelled, GraphResultAccumulator<TKey>? graph = null) => new()
    {
        ResultDetail = _detail,
        InsertedEntities = (IReadOnlyList<InsertedEntity<TKey>>?)_insertedEntities ?? [],
        InsertedIds = (IReadOnlyList<TKey>?)_insertedIds ?? [],
        Failures = (IReadOnlyList<InsertFailure>?)_failures ?? [],
        SuccessCount = _successCount,
        FailureCount = _failureCount,
        WasCancelled = wasCancelled,
        GraphHierarchy = graph?.Hierarchy,
        TraversalInfo = graph?.TraversalInfo
    };
}
