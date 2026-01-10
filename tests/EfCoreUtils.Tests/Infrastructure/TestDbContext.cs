using EfCoreUtils.Tests.Entities;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Tests.Infrastructure;

public class TestDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

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
        var products = ChangeTracker.Entries<Product>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity);

        foreach (var product in products)
        {
            if (product.Price <= 0)
            {
                throw new InvalidOperationException($"Product {product.Id}: Price must be greater than 0");
            }
            if (product.Stock < 0)
            {
                throw new InvalidOperationException($"Product {product.Id}: Stock cannot be negative");
            }
        }
    }
}
