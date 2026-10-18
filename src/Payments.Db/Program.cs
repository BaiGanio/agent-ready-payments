using DbUp;
using DbUp.Engine;

string[] args1 = new[] { "migrate", "--engine", "postgres" };
// 1. Read the args and parse them
//(bool flowControl, int value) = CheckArgs(args);
//if (!flowControl) return value;

// 2. Find the repo root
string root = RepoRoot();

// 3. Connection string per engine. The sa password comes from .env.
string cs = string.Empty;
DotNetEnv.Env.Load(Path.Combine(RepoRoot(), ".env"));
string pass = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
if(args1[2] == "postgres")
    cs = "Host=localhost;Port=5432;Database=payments_labs;Username=labs;Password=labs";
else
    cs = $"Server=localhost,1433;Database=PaymentsLabs;User Id=sa;Password={pass};TrustServerCertificate=true";

// 4. Make the database if it is missing.
if (args1[2] == "postgres")
    EnsureDatabase.For.PostgresqlDatabase(cs);
else
    EnsureDatabase.For.SqlDatabase(cs);

// 5. Build the upgrader. Same four calls for both engines, only the first line differs.
string scriptDir = Path.Combine(root, "db", args1[2]);
CheckScriptDir(scriptDir);

// 6. Run it.
var upgrader = Build(args1[2], cs, scriptDir);
if (args1[0] == "migrate")
{
    var result = upgrader.PerformUpgrade();
    if (result.Successful == false)
    {
        Console.WriteLine(result.Error);
        return 1;
    }
}
else
{


    var scripts = upgrader.GetScriptsToExecute();
    if (scripts.Count == 0)
        Console.WriteLine("No waiting scripts to execute!");
    else
        foreach (var script in scripts)
        {
            Console.WriteLine(script.Name);
        }
}

Console.WriteLine("All good!");
return 0; // Success




static void CheckScriptDir(string scriptDir)
{
    if (!Directory.Exists(scriptDir))
        throw new DirectoryNotFoundException($"No script folder: {scriptDir}");

    if (Directory.GetFiles(scriptDir, "*.sql").Length == 0)
        throw new InvalidOperationException($"No .sql scripts in: {scriptDir}");
}

static UpgradeEngine Build(string engine, string cs, string scriptDir)
{
    var builder = engine == "postgres"
        ? DeployChanges.To.PostgresqlDatabase(cs)
        : DeployChanges.To.SqlDatabase(cs);

    return builder
        .WithScriptsFromFileSystem(scriptDir)   // TODO: throw if the folder is missing or empty
        .WithTransactionPerScript()
        .LogToConsole()
        .Build();
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    
    while (dir != null)
    {
        // Check if Payments.slnx
        if (dir.GetFiles("Payments.slnx").Length > 0) return dir.FullName;
        dir = dir.Parent;
    }
    // If we reach the root of the drive and found nothing, throw an exception
    throw new DirectoryNotFoundException(
        "Could not find repository root. 'Payments.slnx' were not found in any parent directories."
    );
}

static (bool flowControl, int value) CheckArgs(string[] args)
{
    if (args.Length < 3 || args[0] != "migrate" || args[1] != "--engine")
    {
        Console.WriteLine($"Error: invalid or unknown syntax/command '{args[0]}',  or missing or invalid --engine option '{args[1]}'");
        return (flowControl: false, value: 2);
    }

    // Validate engine
    if (args[2] != "postgres" && args[2] != "sqlserver")
    {
        Console.WriteLine($"Error: Unknown engine '{args[2]}'");
        //PrintUsage();
        return (flowControl: false, value: 2);
    }

    return (flowControl: true, value: default);
}