namespace Winnow.Internal.Accumulators;

/// <summary>
/// Accumulates per-entity success and failure data for update/delete operations
/// (which produce <see cref="WinnowResult{TKey}"/>). Allocation is gated by
/// <see cref="ResultDetail"/>.
/// </summary>
internal sealed class WinnowAccumulator<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly ResultDetail _detail;
    private readonly List<TKey>? _successfulIds;
    private readonly List<WinnowFailure<TKey>>? _failures;
    private int _successCount;
    private int _failureCount;

    internal WinnowAccumulator(ResultDetail detail)
    {
        _detail = detail;
        if (detail >= ResultDetail.Minimal)
        {
            _successfulIds = [];
            _failures = [];
        }
    }

    internal void RecordSuccess(TKey id)
    {
        _successCount++;
        if (_detail < ResultDetail.Minimal)
        {
            return;
        }
        _successfulIds!.Add(id);
    }

    internal void RecordFailure(TKey id, string errorMessage, FailureReason reason, Exception? exception)
    {
        _failureCount++;
        if (_detail < ResultDetail.Minimal)
        {
            return;
        }
        _failures!.Add(new WinnowFailure<TKey>
        {
            EntityId = id,
            ErrorMessage = errorMessage,
            Reason = reason,
            Exception = _detail >= ResultDetail.Full ? exception : null
        });
    }

    internal int SuccessCount => _successCount;
    internal int FailureCount => _failureCount;

    internal WinnowResult<TKey> Build(bool wasCancelled, GraphResultAccumulator<TKey>? graph = null) => new()
    {
        ResultDetail = _detail,
        SuccessfulIds = (IReadOnlyList<TKey>?)_successfulIds ?? [],
        Failures = (IReadOnlyList<WinnowFailure<TKey>>?)_failures ?? [],
        SuccessCount = _successCount,
        FailureCount = _failureCount,
        WasCancelled = wasCancelled,
        GraphHierarchy = graph?.Hierarchy,
        TraversalInfo = graph?.TraversalInfo
    };
}
