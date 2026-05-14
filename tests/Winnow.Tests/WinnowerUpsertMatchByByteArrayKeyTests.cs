using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

/// <summary>
/// End-to-end coverage for <c>byte[]</c> as a MatchBy key. <see cref="Winnow.Internal.MatchKey"/>
/// has unit-level structural-equality tests for byte arrays, but the path through
/// <c>ValueHolder&lt;byte[]&gt;</c> + EF Core's BLOB parameter handling has not been exercised
/// end-to-end. Two distinct byte[] instances with identical contents must resolve to the
/// same row.
/// </summary>
public class WinnowerUpsertMatchByByteArrayKeyTests : IDisposable
{
    private readonly BlobKeyContext _context;

    public WinnowerUpsertMatchByByteArrayKeyTests()
    {
        var options = new DbContextOptionsBuilder<BlobKeyContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new BlobKeyContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public void Upsert_MatchBy_ByteArrayKey_FindsExistingRowByContentEquality_UpdatesIt()
    {
        var seededHash = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        _context.Entities.Add(new BlobKeyEntity { HashKey = seededHash, Label = "Seeded" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        // Distinct array instance, same byte contents — MatchKey's structural equality
        // is what makes this match. Without the IStructuralEquatable handling in MatchKey,
        // the dictionary lookup would miss and route this to INSERT.
        var incoming = new BlobKeyEntity
        {
            HashKey = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            Label = "Replacement"
        };

        var saver = new Winnower<BlobKeyEntity, int>(_context);
        var result = saver.Upsert(
            new[] { incoming },
            new UpsertOptions().WithMatchBy<BlobKeyEntity>(e => e.HashKey));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1,
            "byte[] MatchBy must resolve content-equal arrays to the same row.");
        result.InsertedCount.ShouldBe(0);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    public class BlobKeyEntity
    {
        public int Id { get; set; }
        public byte[] HashKey { get; set; } = [];
        public string Label { get; set; } = string.Empty;
    }

    public class BlobKeyContext : DbContext
    {
        public DbSet<BlobKeyEntity> Entities => Set<BlobKeyEntity>();

        public BlobKeyContext(DbContextOptions<BlobKeyContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlobKeyEntity>(e =>
            {
                e.HasKey(x => x.Id);
                // Non-RowVersion blob — `ValueGenerated.Never` so MatchBy accepts it.
                e.Property(x => x.HashKey).IsRequired().HasMaxLength(64);
                e.Property(x => x.Label).IsRequired().HasMaxLength(100);
            });
        }
    }
}
