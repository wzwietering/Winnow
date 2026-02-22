namespace Winnow.Internal;

internal static class EntityPartitioner
{
    internal static List<List<T>> Partition<T>(List<T> items, int maxPartitions)
    {
        if (maxPartitions < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPartitions), "Must be at least 1.");

        if (items is null or { Count: 0 })
            return [];

        var chunkSize = (items.Count + maxPartitions - 1) / maxPartitions;
        var partitions = new List<List<T>>();

        for (var i = 0; i < items.Count; i += chunkSize)
            partitions.Add(items.GetRange(i, Math.Min(chunkSize, items.Count - i)));

        return partitions;
    }

    internal static List<(List<T> Items, int Offset)> PartitionWithOffsets<T>(List<T> items, int maxPartitions)
    {
        var partitions = Partition(items, maxPartitions);
        var result = new List<(List<T>, int)>();
        var offset = 0;

        foreach (var partition in partitions)
        {
            result.Add((partition, offset));
            offset += partition.Count;
        }

        return result;
    }
}
