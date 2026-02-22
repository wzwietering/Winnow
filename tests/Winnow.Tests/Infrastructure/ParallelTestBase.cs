using Winnow.Tests.Entities;
using Microsoft.EntityFrameworkCore;

namespace Winnow.Tests.Infrastructure;

public abstract class ParallelTestBase : TestBase
{
    protected readonly string DbPath = Path.Combine(Path.GetTempPath(), $"winnow_test_{Guid.NewGuid():N}.db");

    protected Func<DbContext> CreateContextFactory()
    {
        return () =>
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite($"DataSource={DbPath}")
                .Options;

            return new TestDbContext(options);
        };
    }

    protected ParallelBatchSaver<Product, int> CreateSaver(int maxDegreeOfParallelism = 2)
    {
        return new ParallelBatchSaver<Product, int>(CreateContextFactory(), maxDegreeOfParallelism);
    }

    protected void EnsureDatabaseCreated()
    {
        using var context = (TestDbContext)CreateContextFactory()();
        context.Database.EnsureCreated();
    }

    protected void SeedWithFactory(Action<TestDbContext> seed)
    {
        using var context = (TestDbContext)CreateContextFactory()();
        seed(context);
    }

    protected List<T> QueryWithFactory<T>(Func<TestDbContext, List<T>> query)
    {
        using var context = (TestDbContext)CreateContextFactory()();
        return query(context);
    }

    protected T QuerySingleWithFactory<T>(Func<TestDbContext, T> query)
    {
        using var context = (TestDbContext)CreateContextFactory()();
        return query(context);
    }

    protected void ResetDatabase()
    {
        using var context = (TestDbContext)CreateContextFactory()();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    public override void Dispose()
    {
        TryDeleteDbFile();
        base.Dispose();
    }

    private void TryDeleteDbFile()
    {
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
