namespace EfCoreUtils.Tests.Entities;

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public byte[] Version { get; set; } = [];
}

public enum OrderStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
