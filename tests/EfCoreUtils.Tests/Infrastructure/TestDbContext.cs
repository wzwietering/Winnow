using EfCoreUtils.Tests.Entities;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Tests.Infrastructure;

public class TestDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Price)
                .HasPrecision(18, 2);
            entity.Property(e => e.Version)
                .IsRequired()
                .IsRowVersion()
                .HasDefaultValue(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber)
                .IsRequired()
                .HasMaxLength(50);
            entity.HasIndex(e => e.OrderNumber)
                .IsUnique();
            entity.Property(e => e.TotalAmount)
                .HasPrecision(18, 2);
            entity.Property(e => e.Version)
                .IsRequired()
                .IsRowVersion()
                .HasDefaultValue(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        });

        modelBuilder.Entity<CustomerOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber)
                .IsRequired()
                .HasMaxLength(50);
            entity.HasIndex(e => e.OrderNumber)
                .IsUnique();
            entity.Property(e => e.CustomerName)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.TotalAmount)
                .HasPrecision(18, 2);
            entity.Property(e => e.Version)
                .IsRequired()
                .IsRowVersion()
                .HasDefaultValue(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

            entity.HasMany(e => e.OrderItems)
                .WithOne(i => i.CustomerOrder)
                .HasForeignKey(i => i.CustomerOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2);
            entity.Property(e => e.Subtotal)
                .HasPrecision(18, 2);
            entity.Property(e => e.Version)
                .IsRequired()
                .IsRowVersion()
                .HasDefaultValue(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        });

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        ValidateEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ValidateEntities()
    {
        ValidateProducts();
        ValidateCustomerOrders();
        ValidateOrderItems();
    }

    private void ValidateProducts()
    {
        var products = ChangeTracker.Entries<Product>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
                throw new InvalidOperationException($"Product {product.Id}: Price must be greater than 0");
            if (product.Stock < 0)
                throw new InvalidOperationException($"Product {product.Id}: Stock cannot be negative");
        }
    }

    private void ValidateCustomerOrders()
    {
        var orders = ChangeTracker.Entries<CustomerOrder>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var order in orders)
        {
            if (order.TotalAmount < 0)
                throw new InvalidOperationException($"CustomerOrder {order.Id}: TotalAmount cannot be negative");
            if (string.IsNullOrWhiteSpace(order.CustomerName))
                throw new InvalidOperationException($"CustomerOrder {order.Id}: CustomerName is required");
        }
    }

    private void ValidateOrderItems()
    {
        var items = ChangeTracker.Entries<OrderItem>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException($"OrderItem {item.Id}: Quantity must be greater than 0");
            if (item.UnitPrice < 0)
                throw new InvalidOperationException($"OrderItem {item.Id}: UnitPrice cannot be negative");
            if (item.Subtotal < 0)
                throw new InvalidOperationException($"OrderItem {item.Id}: Subtotal cannot be negative");

            var expectedSubtotal = item.Quantity * item.UnitPrice;
            if (Math.Abs(item.Subtotal - expectedSubtotal) > 0.01m)
                throw new InvalidOperationException($"OrderItem {item.Id}: Subtotal mismatch");
        }
    }
}
