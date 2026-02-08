using EfCoreUtils.Internal;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchResultMergerTests
{
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(1.5);

    #region MergeBatchResults

    [Fact]
    public void MergeBatchResults_EmptyList_ReturnsEmptyResult()
    {
        var result = BatchResultMerger.MergeBatchResults<int>([], TestDuration, 0);

        result.SuccessfulIds.ShouldBeEmpty();
        result.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void MergeBatchResults_SingleResult_PassesThrough()
    {
        var single = new BatchResult<int>
        {
            SuccessfulIds = [1, 2],
            Failures = [new() { EntityId = 3, ErrorMessage = "fail" }]
        };

        var result = BatchResultMerger.MergeBatchResults([single], TestDuration, 2);

        result.SuccessfulIds.ShouldBe([1, 2]);
        result.Failures.Count.ShouldBe(1);
    }

    [Fact]
    public void MergeBatchResults_TwoResults_ConcatenatesIdsAndFailures()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1, 2] };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [3],
            Failures = [new() { EntityId = 4, ErrorMessage = "err" }]
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 5);

        result.SuccessfulIds.ShouldBe([1, 2, 3]);
        result.Failures.Count.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(4);
    }

    [Fact]
    public void MergeBatchResults_UsesProvidedDuration()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1], Duration = TimeSpan.FromSeconds(10) };

        var result = BatchResultMerger.MergeBatchResults([r1], TestDuration, 1);

        result.Duration.ShouldBe(TestDuration);
    }

    [Fact]
    public void MergeBatchResults_UsesProvidedRoundTrips()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1], DatabaseRoundTrips = 99 };

        var result = BatchResultMerger.MergeBatchResults([r1], TestDuration, 7);

        result.DatabaseRoundTrips.ShouldBe(7);
    }

    [Fact]
    public void MergeBatchResults_AllSuccess_IsCompleteSuccess()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1] };
        var r2 = new BatchResult<int> { SuccessfulIds = [2] };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MergeBatchResults_AllFailure_IsCompleteFailure()
    {
        var r1 = new BatchResult<int>
        {
            Failures = [new() { EntityId = 1, ErrorMessage = "err" }]
        };

        var result = BatchResultMerger.MergeBatchResults([r1], TestDuration, 1);

        result.IsCompleteFailure.ShouldBeTrue();
    }

    [Fact]
    public void MergeBatchResults_Mixed_IsPartialSuccess()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1] };
        var r2 = new BatchResult<int>
        {
            Failures = [new() { EntityId = 2, ErrorMessage = "err" }]
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.IsPartialSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MergeBatchResults_AnyCancelled_WasCancelledTrue()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1] };
        var r2 = new BatchResult<int> { SuccessfulIds = [2], WasCancelled = true };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.WasCancelled.ShouldBeTrue();
    }

    #endregion

    #region MergeInsertResults

    [Fact]
    public void MergeInsertResults_RemapsOriginalIndex()
    {
        var r1 = new InsertBatchResult<int>
        {
            InsertedEntities =
            [
                new() { Id = 10, OriginalIndex = 0, Entity = "a" },
                new() { Id = 11, OriginalIndex = 1, Entity = "b" }
            ]
        };
        var r2 = new InsertBatchResult<int>
        {
            InsertedEntities =
            [
                new() { Id = 20, OriginalIndex = 0, Entity = "c" }
            ]
        };

        var result = BatchResultMerger.MergeInsertResults(
            [(r1, 0), (r2, 5)], TestDuration, 2);

        result.InsertedEntities[0].OriginalIndex.ShouldBe(0);
        result.InsertedEntities[1].OriginalIndex.ShouldBe(1);
        result.InsertedEntities[2].OriginalIndex.ShouldBe(5);
    }

    [Fact]
    public void MergeInsertResults_RemapsFailureEntityIndex()
    {
        var r1 = new InsertBatchResult<int>
        {
            Failures = [new() { EntityIndex = 0, ErrorMessage = "err1" }]
        };
        var r2 = new InsertBatchResult<int>
        {
            Failures = [new() { EntityIndex = 1, ErrorMessage = "err2" }]
        };

        var result = BatchResultMerger.MergeInsertResults(
            [(r1, 0), (r2, 3)], TestDuration, 2);

        result.Failures[0].EntityIndex.ShouldBe(0);
        result.Failures[1].EntityIndex.ShouldBe(4);
    }

    [Fact]
    public void MergeInsertResults_PreservesInsertedIds()
    {
        var r1 = new InsertBatchResult<int>
        {
            InsertedEntities = [new() { Id = 10, OriginalIndex = 0, Entity = "a" }]
        };
        var r2 = new InsertBatchResult<int>
        {
            InsertedEntities = [new() { Id = 20, OriginalIndex = 0, Entity = "b" }]
        };

        var result = BatchResultMerger.MergeInsertResults(
            [(r1, 0), (r2, 1)], TestDuration, 2);

        result.InsertedIds.ShouldBe([10, 20]);
    }

    #endregion

    #region MergeUpsertResults

    [Fact]
    public void MergeUpsertResults_RemapsInsertedOriginalIndex()
    {
        var r1 = new UpsertBatchResult<int>
        {
            InsertedEntities =
            [
                new() { Id = 10, OriginalIndex = 0, Entity = "a", Operation = UpsertOperationType.Insert }
            ]
        };
        var r2 = new UpsertBatchResult<int>
        {
            InsertedEntities =
            [
                new() { Id = 20, OriginalIndex = 0, Entity = "b", Operation = UpsertOperationType.Insert }
            ]
        };

        var result = BatchResultMerger.MergeUpsertResults(
            [(r1, 0), (r2, 5)], TestDuration, 2);

        result.InsertedEntities[0].OriginalIndex.ShouldBe(0);
        result.InsertedEntities[1].OriginalIndex.ShouldBe(5);
    }

    [Fact]
    public void MergeUpsertResults_RemapsUpdatedOriginalIndex()
    {
        var r1 = new UpsertBatchResult<int>
        {
            UpdatedEntities =
            [
                new() { Id = 1, OriginalIndex = 2, Entity = "a", Operation = UpsertOperationType.Update }
            ]
        };

        var result = BatchResultMerger.MergeUpsertResults(
            [(r1, 10)], TestDuration, 1);

        result.UpdatedEntities[0].OriginalIndex.ShouldBe(12);
    }

    [Fact]
    public void MergeUpsertResults_RemapsFailureEntityIndex()
    {
        var r1 = new UpsertBatchResult<int>
        {
            Failures =
            [
                new() { EntityIndex = 0, ErrorMessage = "err", AttemptedOperation = UpsertOperationType.Insert }
            ]
        };

        var result = BatchResultMerger.MergeUpsertResults(
            [(r1, 7)], TestDuration, 1);

        result.Failures[0].EntityIndex.ShouldBe(7);
    }

    #endregion

    #region Graph Merging

    [Fact]
    public void MergeBatchResults_TraversalInfo_SumsTotalEntitiesTraversed()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            TraversalInfo = new() { TotalEntitiesTraversed = 5, MaxDepthReached = 1 }
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            TraversalInfo = new() { TotalEntitiesTraversed = 3, MaxDepthReached = 2 }
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.TotalEntitiesTraversed.ShouldBe(8);
    }

    [Fact]
    public void MergeBatchResults_TraversalInfo_TakesMaxDepth()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            TraversalInfo = new() { MaxDepthReached = 3 }
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            TraversalInfo = new() { MaxDepthReached = 1 }
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.TraversalInfo!.MaxDepthReached.ShouldBe(3);
    }

    [Fact]
    public void MergeBatchResults_TraversalInfo_MergesEntitiesByDepth()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            TraversalInfo = new()
            {
                EntitiesByDepth = new Dictionary<int, int> { [0] = 2, [1] = 3 }
            }
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            TraversalInfo = new()
            {
                EntitiesByDepth = new Dictionary<int, int> { [0] = 1, [2] = 5 }
            }
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.TraversalInfo!.EntitiesByDepth[0].ShouldBe(3);
        result.TraversalInfo!.EntitiesByDepth[1].ShouldBe(3);
        result.TraversalInfo!.EntitiesByDepth[2].ShouldBe(5);
    }

    [Fact]
    public void MergeBatchResults_TraversalInfo_SumsJoinRecords()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            TraversalInfo = new() { JoinRecordsCreated = 4, JoinRecordsRemoved = 1 }
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            TraversalInfo = new() { JoinRecordsCreated = 2, JoinRecordsRemoved = 3 }
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(6);
        result.TraversalInfo!.JoinRecordsRemoved.ShouldBe(4);
    }

    [Fact]
    public void MergeBatchResults_TraversalInfo_MergesJoinOperationsByNavigation()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            TraversalInfo = new()
            {
                JoinOperationsByNavigation = new Dictionary<string, (int, int)>
                {
                    ["Student.Courses"] = (3, 1)
                }
            }
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            TraversalInfo = new()
            {
                JoinOperationsByNavigation = new Dictionary<string, (int, int)>
                {
                    ["Student.Courses"] = (2, 0),
                    ["Course.Students"] = (1, 1)
                }
            }
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.TraversalInfo!.JoinOperationsByNavigation["Student.Courses"].ShouldBe((5, 1));
        result.TraversalInfo!.JoinOperationsByNavigation["Course.Students"].ShouldBe((1, 1));
    }

    [Fact]
    public void MergeBatchResults_GraphHierarchy_ConcatenatesNodes()
    {
        var r1 = new BatchResult<int>
        {
            SuccessfulIds = [1],
            GraphHierarchy = [new() { EntityId = 1, EntityType = "A", Depth = 0 }]
        };
        var r2 = new BatchResult<int>
        {
            SuccessfulIds = [2],
            GraphHierarchy = [new() { EntityId = 2, EntityType = "B", Depth = 0 }]
        };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(2);
    }

    [Fact]
    public void MergeBatchResults_GraphHierarchy_AllNull_ReturnsNull()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1] };
        var r2 = new BatchResult<int> { SuccessfulIds = [2] };

        var result = BatchResultMerger.MergeBatchResults([r1, r2], TestDuration, 2);

        result.GraphHierarchy.ShouldBeNull();
    }

    [Fact]
    public void MergeBatchResults_TraversalInfo_AllNull_ReturnsNull()
    {
        var r1 = new BatchResult<int> { SuccessfulIds = [1] };

        var result = BatchResultMerger.MergeBatchResults([r1], TestDuration, 1);

        result.TraversalInfo.ShouldBeNull();
    }

    #endregion
}
