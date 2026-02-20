using EfCoreUtils.Benchmarks.Entities;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Infrastructure;

public class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : DbContext(options)
{
    public DbSet<BenchmarkProduct> Products => Set<BenchmarkProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenchmarkProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        base.OnModelCreating(modelBuilder);
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
