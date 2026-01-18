namespace EfCoreUtils.Tests.Entities;

public class CustomerOrderWithGuidGrandchildren
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public CustomerOrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public byte[] Version { get; set; } = [];

    public ICollection<OrderItemWithGuidReservations> OrderItems { get; set; } = [];
}
