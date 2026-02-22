namespace Winnow.Tests.Entities;

/// <summary>
/// Common interface for product entities to enable generic validation.
/// </summary>
public interface IProductEntity
{
    decimal Price { get; }
    int Stock { get; }
    string DisplayId { get; }
}
