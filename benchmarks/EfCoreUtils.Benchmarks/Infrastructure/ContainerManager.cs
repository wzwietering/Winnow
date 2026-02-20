using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace EfCoreUtils.Benchmarks.Infrastructure;

public sealed class ContainerManager : IAsyncDisposable
{
    private readonly Dictionary<DatabaseProvider, string> _connectionStrings = [];
    private PostgreSqlContainer? _postgresContainer;
    private MsSqlContainer? _sqlServerContainer;
    private string? _sqliteTempFile;

    public async Task StartAsync(IEnumerable<DatabaseProvider> providers)
    {
        var tasks = new List<Task>();

        foreach (var provider in providers.Distinct())
        {
            tasks.Add(provider switch
            {
                DatabaseProvider.Sqlite => StartSqliteAsync(),
                DatabaseProvider.PostgreSql => StartPostgreSqlAsync(),
                DatabaseProvider.SqlServer => StartSqlServerAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            });
        }

        await Task.WhenAll(tasks);

        // BenchmarkDotNet runs benchmarks in child processes — pass connection strings via env vars
        foreach (var (provider, cs) in _connectionStrings)
            Environment.SetEnvironmentVariable(EnvVarName(provider), cs);
    }

    public IReadOnlyCollection<DatabaseProvider> StartedProviders =>
        _connectionStrings.Count > 0
            ? _connectionStrings.Keys
            : GetProvidersFromEnvironment();

    public string GetConnectionString(DatabaseProvider provider)
    {
        if (_connectionStrings.TryGetValue(provider, out var cs))
            return cs;

        // Child process: read from inherited environment variable
        var envCs = Environment.GetEnvironmentVariable(EnvVarName(provider));
        if (envCs is not null)
            return envCs;

        throw new InvalidOperationException($"Provider {provider} was not started");
    }

    private Task StartSqliteAsync()
    {
        _sqliteTempFile = Path.GetTempFileName();
        _connectionStrings[DatabaseProvider.Sqlite] = $"DataSource={_sqliteTempFile}";
        return Task.CompletedTask;
    }

    private async Task StartPostgreSqlAsync()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
            .Build();

        await _postgresContainer.StartAsync();
        _connectionStrings[DatabaseProvider.PostgreSql] = _postgresContainer.GetConnectionString();
    }

    private async Task StartSqlServerAsync()
    {
        _sqlServerContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sqlServerContainer.StartAsync();
        _connectionStrings[DatabaseProvider.SqlServer] = _sqlServerContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgresContainer is not null)
            await _postgresContainer.DisposeAsync();

        if (_sqlServerContainer is not null)
            await _sqlServerContainer.DisposeAsync();

        if (_sqliteTempFile is not null && File.Exists(_sqliteTempFile))
            File.Delete(_sqliteTempFile);
    }

    private static string EnvVarName(DatabaseProvider provider) =>
        $"BENCHMARK_{provider.ToString().ToUpperInvariant()}_CS";

    private static List<DatabaseProvider> GetProvidersFromEnvironment() =>
        Enum.GetValues<DatabaseProvider>()
            .Where(p => Environment.GetEnvironmentVariable(EnvVarName(p)) is not null)
            .ToList();
}
