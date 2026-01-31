namespace EfCoreUtils.Tests.Entities;

public class OrderLine
{
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int? ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public byte[] Version { get; set; } = [];

    public CustomerOrder Order { get; set; } = null!;
    public Product? Product { get; set; }
    public ICollection<OrderLineNote> Notes { get; set; } = [];
}
