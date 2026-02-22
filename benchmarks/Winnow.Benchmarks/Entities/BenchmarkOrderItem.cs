namespace Winnow.Benchmarks.Entities;

public class BenchmarkOrderItem
{
    public int Id { get; set; }
    public int BenchmarkOrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public BenchmarkOrder Order { get; set; } = null!;
    public ICollection<BenchmarkOrderReservation> Reservations { get; set; } = [];
}
