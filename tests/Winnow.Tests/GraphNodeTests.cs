using Shouldly;

namespace Winnow.Tests;

public class GraphNodeTests
{
    [Fact]
    public void GetAllDescendantIds_NoChildren_ReturnsEmpty()
    {
        var node = new GraphNode<int> { EntityId = 1 };

        node.GetAllDescendantIds().ShouldBeEmpty();
    }

    [Fact]
    public void GetAllDescendantIds_FlatChildren_ReturnsAllChildIds()
    {
        var node = new GraphNode<int>
        {
            EntityId = 1,
            Children =
            [
                new() { EntityId = 2 },
                new() { EntityId = 3 }
            ]
        };

        var ids = node.GetAllDescendantIds();
        ids.ShouldBe([2, 3]);
    }

    [Fact]
    public void GetAllDescendantIds_NestedChildren_ReturnsAll()
    {
        var node = new GraphNode<int>
        {
            EntityId = 1,
            Children =
            [
                new()
                {
                    EntityId = 2,
                    Children = [new() { EntityId = 4 }]
                },
                new() { EntityId = 3 }
            ]
        };

        var ids = node.GetAllDescendantIds();
        ids.ShouldBe([2, 4, 3]);
    }

    [Fact]
    public void GetAllDescendantIds_CyclicReference_DoesNotInfiniteLoop()
    {
        var child = new GraphNode<int> { EntityId = 2 };
        var root = new GraphNode<int>
        {
            EntityId = 1,
            Children = [child]
        };
        // Create a cycle: child points back to root
        child = new GraphNode<int>
        {
            EntityId = 2,
            Children = [root]
        };
        var cyclicRoot = new GraphNode<int>
        {
            EntityId = 1,
            Children = [child]
        };

        var ids = cyclicRoot.GetAllDescendantIds();

        ids.ShouldContain(2);
        ids.ShouldContain(1);
    }

    [Fact]
    public void GetChildIds_ReturnsOnlyDirectChildren()
    {
        var node = new GraphNode<int>
        {
            EntityId = 1,
            Children =
            [
                new()
                {
                    EntityId = 2,
                    Children = [new() { EntityId = 4 }]
                },
                new() { EntityId = 3 }
            ]
        };

        var ids = node.GetChildIds();
        ids.ShouldBe([2, 3]);
    }
}
