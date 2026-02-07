using EfCoreUtils.Internal;
using Shouldly;

namespace EfCoreUtils.Tests;

public class EntityPartitionerTests
{
    [Fact]
    public void Partition_EmptyList_ReturnsEmptyResult()
    {
        var result = EntityPartitioner.Partition(new List<int>(), 4);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Partition_NullList_ReturnsEmptyResult()
    {
        var result = EntityPartitioner.Partition<int>(null!, 4);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Partition_SingleItem_ReturnsSinglePartition()
    {
        var result = EntityPartitioner.Partition([42], 4);

        result.Count.ShouldBe(1);
        result[0].ShouldBe([42]);
    }

    [Fact]
    public void Partition_CountLessThanMax_OnePartitionPerItem()
    {
        var result = EntityPartitioner.Partition([1, 2, 3], 10);

        result.Count.ShouldBe(3);
        result[0].ShouldBe([1]);
        result[1].ShouldBe([2]);
        result[2].ShouldBe([3]);
    }

    [Fact]
    public void Partition_CountEqualsMax_EqualSingleItemPartitions()
    {
        var result = EntityPartitioner.Partition([1, 2, 3, 4], 4);

        result.Count.ShouldBe(4);
        result.ShouldAllBe(p => p.Count == 1);
    }

    [Fact]
    public void Partition_EvenlyDivisible_EqualPartitions()
    {
        var result = EntityPartitioner.Partition([1, 2, 3, 4, 5, 6], 3);

        result.Count.ShouldBe(3);
        result.ShouldAllBe(p => p.Count == 2);
    }

    [Fact]
    public void Partition_NotEvenlyDivisible_LastPartitionGetsRemainder()
    {
        var result = EntityPartitioner.Partition([1, 2, 3, 4, 5], 2);

        result.Count.ShouldBe(2);
        result[0].ShouldBe([1, 2, 3]);
        result[1].ShouldBe([4, 5]);
    }

    [Fact]
    public void Partition_LargeCollection_NoEntitiesLost()
    {
        var items = Enumerable.Range(1, 1000).ToList();

        var result = EntityPartitioner.Partition(items, 4);

        result.SelectMany(p => p).ToList().ShouldBe(items);
    }

    [Fact]
    public void Partition_LargeCollection_OrderPreserved()
    {
        var items = Enumerable.Range(1, 1000).ToList();

        var result = EntityPartitioner.Partition(items, 4);
        var flattened = result.SelectMany(p => p).ToList();

        flattened.ShouldBe(items);
    }

    [Fact]
    public void Partition_MaxPartitionsOne_SinglePartitionWithAllItems()
    {
        var items = new List<int> { 1, 2, 3, 4, 5 };

        var result = EntityPartitioner.Partition(items, 1);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Partition_MaxPartitionsLessThanOne_Throws(int maxPartitions)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => EntityPartitioner.Partition([1, 2], maxPartitions));
    }

    [Fact]
    public void PartitionWithOffsets_OffsetsAreCorrect()
    {
        var result = EntityPartitioner.PartitionWithOffsets([1, 2, 3, 4, 5], 2);

        result[0].Offset.ShouldBe(0);
        result[1].Offset.ShouldBe(3);
    }

    [Fact]
    public void PartitionWithOffsets_ThreePartitions_OffsetsAccumulate()
    {
        var result = EntityPartitioner.PartitionWithOffsets([1, 2, 3, 4, 5, 6], 3);

        result[0].Offset.ShouldBe(0);
        result[1].Offset.ShouldBe(2);
        result[2].Offset.ShouldBe(4);
    }

    [Fact]
    public void PartitionWithOffsets_ItemsMatchPartition()
    {
        var result = EntityPartitioner.PartitionWithOffsets([10, 20, 30, 40, 50], 2);

        result[0].Items.ShouldBe([10, 20, 30]);
        result[1].Items.ShouldBe([40, 50]);
    }

    [Fact]
    public void PartitionWithOffsets_FlattenPreservesOrder()
    {
        var items = Enumerable.Range(1, 100).ToList();

        var result = EntityPartitioner.PartitionWithOffsets(items, 7);
        var flattened = result.SelectMany(p => p.Items).ToList();

        flattened.ShouldBe(items);
    }
}
