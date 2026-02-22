using BenchmarkDotNet.Running;
using Winnow.Benchmarks;
using Winnow.Benchmarks.Infrastructure;

// BenchmarkDotNet searches CWD for .sln/.csproj but doesn't recognize .slnx
var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
if (File.Exists(Path.Combine(projectDir, "Winnow.Benchmarks.csproj")))
    Environment.CurrentDirectory = projectDir;

var sqliteOnly = args.Contains("--sqlite-only");
var noContainers = args.Contains("--no-containers");

// Strip custom args before passing to BenchmarkDotNet
var bdnArgs = args
    .Where(a => a is not "--sqlite-only" and not "--no-containers")
    .ToArray();

var providers = DetermineProviders(sqliteOnly, noContainers);

try
{
    Console.WriteLine($"Starting database providers: {string.Join(", ", providers)}");
    await GlobalState.Containers.StartAsync(providers);
    Console.WriteLine("All providers ready.");

    BenchmarkSwitcher
        .FromAssembly(typeof(Program).Assembly)
        .Run(bdnArgs);
}
finally
{
    await GlobalState.Containers.DisposeAsync();
}

static List<DatabaseProvider> DetermineProviders(bool sqliteOnly, bool noContainers)
{
    if (sqliteOnly || noContainers)
        return [DatabaseProvider.Sqlite];

    return [DatabaseProvider.Sqlite, DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer];
}
