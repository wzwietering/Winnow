using Shouldly;

namespace Winnow.Tests;

public class GraphTraversalResultTests
{
    [Fact]
    public void Default_Properties_HaveExpectedValues()
    {
        var result = new GraphTraversalResult<int>();

        result.MaxDepthReached.ShouldBe(0);
        result.TotalEntitiesTraversed.ShouldBe(0);
        result.EntitiesByDepth.ShouldBeEmpty();
        result.ProcessedReferencesByType.ShouldBeEmpty();
        result.UniqueReferencesProcessed.ShouldBe(0);
        result.MaxReferenceDepthReached.ShouldBe(0);
        result.JoinRecordsCreated.ShouldBe(0);
        result.JoinRecordsRemoved.ShouldBe(0);
        result.JoinOperationsByNavigation.ShouldBeEmpty();
    }

    [Fact]
    public void InitProperties_RetainValues()
    {
        var result = new GraphTraversalResult<int>
        {
            MaxDepthReached = 3,
            TotalEntitiesTraversed = 10,
            EntitiesByDepth = new Dictionary<int, int> { [0] = 1, [1] = 4, [2] = 5 },
            UniqueReferencesProcessed = 2,
            MaxReferenceDepthReached = 1,
            JoinRecordsCreated = 5,
            JoinRecordsRemoved = 2,
            JoinOperationsByNavigation = new Dictionary<string, (int, int)>
            {
                ["Student.Courses"] = (3, 1)
            }
        };

        result.MaxDepthReached.ShouldBe(3);
        result.TotalEntitiesTraversed.ShouldBe(10);
        result.EntitiesByDepth.Count.ShouldBe(3);
        result.JoinRecordsCreated.ShouldBe(5);
        result.JoinRecordsRemoved.ShouldBe(2);
        result.JoinOperationsByNavigation["Student.Courses"].ShouldBe((3, 1));
    }

    [Fact]
    public void StringKey_Works()
    {
        var result = new GraphTraversalResult<string>
        {
            ProcessedReferencesByType = new Dictionary<string, IReadOnlyList<string>>
            {
                ["Category"] = ["cat-1", "cat-2"]
            }
        };

        result.ProcessedReferencesByType["Category"].Count.ShouldBe(2);
    }
}
