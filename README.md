# EfCoreUtils
The goal of this project is to investigate and implement efficient EF Core database algorithms. Several research questions will be tested:
- What is the fastest way to update many entities while tracking the failed entity ids and saving the successful entity ids?
- What is the best way to track the changed properties of updated entities?

## Research Question 1: Batch Update Performance

### Strategies Tested
We tested two batch update strategies:

1. **One-by-One**: Process each entity individually with separate SaveChanges calls
   - Guaranteed isolation: one failure doesn't affect others
   - Predictable: Always N database round trips for N entities
   - Simple to understand and debug

2. **Divide-and-Conquer**: Binary search approach to isolate failures
   - Optimistic: Attempts to save all entities at once
   - On failure: Splits batch in half and recursively processes each half
   - Efficient for low failure rates: O(log N) splits + F failures
   - Worst case (all failures): 2N-1 round trips

### Performance Results (1000 entities, SQLite in-memory)

| Failure Rate | One-by-One Time | D&C Time | One-by-One Trips | D&C Trips | Winner (Time) |
|--------------|-----------------|----------|------------------|-----------|---------------|
| 0%           | 79.8ms          | 103.8ms  | 1000             | 1         | One-by-One    |
| 1%           | 87.8ms          | 127.2ms  | 1000             | 147       | One-by-One    |
| 5%           | 87.0ms          | 146.3ms  | 1000             | 523       | One-by-One    |
| 25%          | 103.2ms         | 190.2ms  | 1000             | 1499      | One-by-One    |
| 50%          | 83.4ms          | 272.9ms  | 1000             | 1999      | One-by-One    |
| 100%         | 1.9ms           | 9.6ms    | 100              | 199       | One-by-One    |

### Key Findings

1. **Database Round Trips**: Divide-and-conquer dramatically reduces database round trips compared to one-by-one:
   - 0% failures: 1 trip vs 1000 (1000x improvement!)
   - 1% failures: 147 trips vs 1000 (6.8x improvement)
   - 5% failures: 523 trips vs 1000 (1.9x improvement)

2. **Actual Performance (SQLite)**: Counter-intuitively, one-by-one was faster in our SQLite tests
   - SQLite in-memory is extremely fast with negligible latency
   - The overhead of recursive calls and transaction management outweighs the benefit of fewer round trips
   - The 100% failure case shows the worst-case scenario: divide-and-conquer is 5x slower

3. **Real-World Implications** (with network databases like SQL Server):
   - **0-5% failure rate**: Divide-and-conquer should win significantly due to network latency savings
   - **High failure rates (>25%)**: One-by-one becomes more competitive
   - **100% failure rate**: One-by-one is clearly better

### Recommendation

- **For network-based databases (SQL Server, PostgreSQL)**: Use divide-and-conquer for typical scenarios (0-10% failure rate)
- **For extremely fast local databases (SQLite)**: Use one-by-one for simplicity
- **For high failure rate scenarios (>50%)**: Use one-by-one regardless of database
- **For critical systems**: Consider implementing adaptive strategy selection based on observed failure rates

### Implementation Details

The `BatchSaver<TEntity>` class implements both strategies:
```csharp
var saver = new BatchSaver<Product>(context);

// Use one-by-one strategy (default)
var result = saver.UpdateBatch(products, new BatchOptions
{
    Strategy = BatchStrategy.OneByOne
});

// Use divide-and-conquer strategy
var result = saver.UpdateBatch(products, new BatchOptions
{
    Strategy = BatchStrategy.DivideAndConquer
});
```

The `BatchResult` class provides detailed tracking:
- `SuccessfulIds`: IDs of entities that were saved successfully
- `Failures`: Detailed information about each failed entity including ID, reason, and exception
- `DatabaseRoundTrips`: Number of database calls made
- `Duration`: Total time taken
- `IsCompleteSuccess`, `IsPartialSuccess`, `IsCompleteFailure`: Status helpers
