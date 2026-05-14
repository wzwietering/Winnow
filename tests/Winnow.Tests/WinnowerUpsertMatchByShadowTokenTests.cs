using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

/// <summary>
/// Shadow concurrency tokens have no CLR property to copy from, so MatchBy cannot
/// support them. The contract is to surface this as a configuration error at
/// <c>ResolveBatch</c> time — before any entity is processed — rather than as a
/// per-entity failure mid-batch.
/// </summary>
public class WinnowerUpsertMatchByShadowTokenTests : IDisposable
{
    private readonly ShadowTokenContext _context;

    public WinnowerUpsertMatchByShadowTokenTests()
    {
        var options = new DbContextOptionsBuilder<ShadowTokenContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new ShadowTokenContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Upsert_MatchBy_EntityWithShadowConcurrencyToken_ThrowsBeforeEntityProcessingBegins()
    {
        // Seed one row so MatchBy resolution proceeds far enough to discover the
        // shadow token via CollectConcurrencyTokens. Without the early-validation
        // fix, the error surfaces from the per-entity copy path, recorded as a
        // failure on the accumulator. The contract: throw at ResolveBatch time
        // and never reach per-entity processing.
        _context.Items.Add(new ShadowTokenEntity { Sku = "SKU-1", Name = "seed" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var saver = new Winnower<ShadowTokenEntity, int>(_context);
        var batch = new[]
        {
            new ShadowTokenEntity { Sku = "SKU-1", Name = "updated" }
        };
        var options = new UpsertOptions().WithMatchBy<ShadowTokenEntity>(e => e.Sku);

        // Sanity check: the shadow token actually exists in the model.
        var entityType = _context.Model.FindEntityType(typeof(ShadowTokenEntity))!;
        var shadowProp = entityType.FindProperty("RowStamp")!;
        shadowProp.IsConcurrencyToken.ShouldBeTrue("Test setup: shadow RowStamp must be a concurrency token.");
        shadowProp.PropertyInfo.ShouldBeNull("Test setup: shadow RowStamp must have null PropertyInfo.");

        // The shadow concurrency token is a configuration error — MatchBy cannot
        // copy values to/from a property with no CLR accessor. The error must
        // propagate out of Upsert (raised in ResolveBatch) rather than being
        // swallowed into a per-entity failure mid-batch.
        var ex = Should.Throw<InvalidOperationException>(() => saver.Upsert(batch, options));
        ex.Message.ShouldContain("shadow", Shouldly.Case.Insensitive,
            "Error must clearly identify the shadow-property root cause.");
    }
}

public class ShadowTokenContext : DbContext
{
    public ShadowTokenContext(DbContextOptions options) : base(options) { }

    public DbSet<ShadowTokenEntity> Items => Set<ShadowTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ShadowTokenEntity>();
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Sku).IsRequired();
        entity.Property(e => e.Name);
        // Configure RowStamp as a shadow concurrency token — no CLR property exists.
        entity.Property<byte[]>("RowStamp").IsRowVersion().IsConcurrencyToken();
    }
}

public class ShadowTokenEntity
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Name { get; set; }
}
