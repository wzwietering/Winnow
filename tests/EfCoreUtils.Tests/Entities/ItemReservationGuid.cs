namespace EfCoreUtils.Tests.Entities;

public class ItemReservationGuid
{
    public Guid Id { get; set; }
    public int OrderItemWithGuidReservationsId { get; set; }
    public string WarehouseLocation { get; set; } = string.Empty;
    public int ReservedQuantity { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public OrderItemWithGuidReservations OrderItem { get; set; } = null!;
}
