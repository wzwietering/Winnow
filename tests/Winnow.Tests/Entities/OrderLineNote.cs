namespace Winnow.Tests.Entities;

public class OrderLineNote
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public OrderLine OrderLine { get; set; } = null!;
}
