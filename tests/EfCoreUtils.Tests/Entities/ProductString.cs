namespace EfCoreUtils.Tests.Entities;

public class ProductString : IProductEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public byte[] Version { get; set; } = [];

    public string DisplayId => Id;
}
