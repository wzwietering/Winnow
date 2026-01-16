namespace EfCoreUtils.Tests.Entities;

public class ProductGuid
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public byte[] Version { get; set; } = [];
}
