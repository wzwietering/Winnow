namespace EfCoreUtils.Benchmarks.Entities;

public class BenchmarkOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public ICollection<BenchmarkOrderItem> Items { get; set; } = [];
}
