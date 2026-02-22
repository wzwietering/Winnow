namespace Winnow.Tests.Entities;

public class InventoryLocation
{
    public string WarehouseCode { get; set; } = string.Empty;
    public int AisleNumber { get; set; }
    public string BinCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime LastUpdated { get; set; }
    public byte[] Version { get; set; } = [];
}
