namespace Winnow.Benchmarks.Entities;

public class BenchmarkOrderReservation
{
    public int Id { get; set; }
    public int BenchmarkOrderItemId { get; set; }
    public string WarehouseLocation { get; set; } = string.Empty;
    public int ReservedQuantity { get; set; }
    public BenchmarkOrderItem OrderItem { get; set; } = null!;
}
