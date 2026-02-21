using EfCoreUtils.Benchmarks.Entities;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Infrastructure;

public class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : DbContext(options)
{
    public DbSet<BenchmarkProduct> Products => Set<BenchmarkProduct>();
    public DbSet<BenchmarkOrder> Orders => Set<BenchmarkOrder>();
    public DbSet<BenchmarkOrderItem> OrderItems => Set<BenchmarkOrderItem>();
    public DbSet<BenchmarkOrderReservation> OrderReservations => Set<BenchmarkOrderReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureProduct(modelBuilder);
        ConfigureOrderGraph(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureProduct(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenchmarkProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });
    }

    private static void ConfigureOrderGraph(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenchmarkOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.HasMany(e => e.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.BenchmarkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BenchmarkOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.HasMany(e => e.Reservations)
                .WithOne(r => r.OrderItem)
                .HasForeignKey(r => r.BenchmarkOrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BenchmarkOrderReservation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WarehouseLocation).IsRequired().HasMaxLength(50);
        });
    }

    public override int SaveChanges()
    {
        ValidateProducts();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateProducts();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ValidateProducts()
    {
        var products = ChangeTracker.Entries<BenchmarkProduct>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
                throw new InvalidOperationException($"Product {product.Id}: Price must be greater than 0");
        }
    }
}
