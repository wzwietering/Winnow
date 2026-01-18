namespace EfCoreUtils.Tests.Entities;

public class OrderItemWithGuidReservations
{
    public int Id { get; set; }
    public int CustomerOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
    public byte[] Version { get; set; } = [];

    public CustomerOrderWithGuidGrandchildren CustomerOrder { get; set; } = null!;
    public ICollection<ItemReservationGuid> Reservations { get; set; } = [];
}
