namespace Winnow.Tests.Entities;

public class ItemReservation
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public string WarehouseLocation { get; set; } = string.Empty;
    public int ReservedQuantity { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public OrderItem OrderItem { get; set; } = null!;
}
