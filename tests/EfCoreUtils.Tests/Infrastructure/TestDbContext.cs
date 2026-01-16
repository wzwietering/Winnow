using EfCoreUtils.Tests.Entities;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Tests.Infrastructure;

public class TestDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductLong> ProductLongs => Set<ProductLong>();
    public DbSet<ProductGuid> ProductGuids => Set<ProductGuid>();
    public DbSet<ProductString> ProductStrings => Set<ProductString>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ItemReservation> ItemReservations => Set<ItemReservation>();

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

        modelBuilder.Entity<ProductLong>(entity =>
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

        modelBuilder.Entity<ProductGuid>(entity =>
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

        modelBuilder.Entity<ProductString>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasMaxLength(50);
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

            entity.HasMany(e => e.Reservations)
                .WithOne(r => r.OrderItem)
                .HasForeignKey(r => r.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemReservation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WarehouseLocation)
                .IsRequired()
                .HasMaxLength(100);
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
        ValidateProductLongs();
        ValidateProductGuids();
        ValidateProductStrings();
        ValidateCustomerOrders();
        ValidateOrderItems();
        ValidateItemReservations();
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

    private void ValidateProductLongs()
    {
        var products = ChangeTracker.Entries<ProductLong>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
                throw new InvalidOperationException($"ProductLong {product.Id}: Price must be greater than 0");
            if (product.Stock < 0)
                throw new InvalidOperationException($"ProductLong {product.Id}: Stock cannot be negative");
        }
    }

    private void ValidateProductGuids()
    {
        var products = ChangeTracker.Entries<ProductGuid>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
                throw new InvalidOperationException($"ProductGuid {product.Id}: Price must be greater than 0");
            if (product.Stock < 0)
                throw new InvalidOperationException($"ProductGuid {product.Id}: Stock cannot be negative");
        }
    }

    private void ValidateProductStrings()
    {
        var products = ChangeTracker.Entries<ProductString>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
                throw new InvalidOperationException($"ProductString {product.Id}: Price must be greater than 0");
            if (product.Stock < 0)
                throw new InvalidOperationException($"ProductString {product.Id}: Stock cannot be negative");
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

    private void ValidateItemReservations()
    {
        var reservations = ChangeTracker.Entries<ItemReservation>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var reservation in reservations)
        {
            if (reservation.ReservedQuantity <= 0)
                throw new InvalidOperationException(
                    $"ItemReservation {reservation.Id}: ReservedQuantity must be greater than 0");
            if (string.IsNullOrWhiteSpace(reservation.WarehouseLocation))
                throw new InvalidOperationException(
                    $"ItemReservation {reservation.Id}: WarehouseLocation is required");
        }
    }
}
