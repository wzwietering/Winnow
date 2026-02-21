using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Benchmarks.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    PostgreSql,
    SqlServer
}

public static class DatabaseProviderFactory
{
    public static DbContextOptions<BenchmarkDbContext> CreateOptions(
        DatabaseProvider provider,
        string connectionString) =>
        provider switch
        {
            DatabaseProvider.Sqlite => new DbContextOptionsBuilder<BenchmarkDbContext>()
                .UseSqlite(connectionString)
                .Options,
            DatabaseProvider.PostgreSql => new DbContextOptionsBuilder<BenchmarkDbContext>()
                .UseNpgsql(connectionString)
                .Options,
            DatabaseProvider.SqlServer => new DbContextOptionsBuilder<BenchmarkDbContext>()
                .UseSqlServer(connectionString)
                .Options,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
}
