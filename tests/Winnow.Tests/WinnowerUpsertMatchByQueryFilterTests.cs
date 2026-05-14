using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

/// <summary>
/// MatchBy refuses to run against entity types that have a global query filter:
/// AsNoTracking does not suppress the filter, so MatchBy would silently miss
/// soft-deleted or tenant-scoped rows and route existing entities to INSERT —
/// producing duplicates. The library fails fast with a clear mitigation message
/// rather than corrupting data quietly.
/// </summary>
public class WinnowerUpsertMatchByQueryFilterTests : IDisposable
{
    private readonly FilteredContext _context;

    public WinnowerUpsertMatchByQueryFilterTests()
    {
        var options = new DbContextOptionsBuilder<FilteredContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new FilteredContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public void Upsert_MatchBy_OnEntityWithGlobalQueryFilter_FailsFast()
    {
        var saver = new Winnower<FilteredEntity, int>(_context);
        var batch = new[] { new FilteredEntity { Sku = "S1", Name = "X" } };
        var options = new UpsertOptions().WithMatchBy<FilteredEntity>(f => f.Sku);

        var ex = Should.Throw<InvalidOperationException>(() => saver.Upsert(batch, options));

        ex.Message.ShouldContain(nameof(FilteredEntity));
        ex.Message.ShouldContain("filter", Case.Insensitive);
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_OnEntityWithGlobalQueryFilter_FailsFast()
    {
        var saver = new Winnower<FilteredEntity, int>(_context);
        var batch = new[] { new FilteredEntity { Sku = "S1", Name = "X" } };
        var options = new UpsertOptions().WithMatchBy<FilteredEntity>(f => f.Sku);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await saver.UpsertAsync(batch, options));

        ex.Message.ShouldContain(nameof(FilteredEntity));
        ex.Message.ShouldContain("filter", Case.Insensitive);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    public class FilteredEntity
    {
        public int Id { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    public class FilteredContext : DbContext
    {
        public DbSet<FilteredEntity> Entities => Set<FilteredEntity>();

        public FilteredContext(DbContextOptions<FilteredContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FilteredEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Sku).IsRequired().HasMaxLength(50);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                // Soft-delete filter — exactly the kind that silently breaks MatchBy.
                e.HasQueryFilter(x => !x.IsDeleted);
            });
        }
    }
}
