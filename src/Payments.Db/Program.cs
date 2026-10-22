using DbUp;
using DbUp.Engine;

// 1. Read the args and parse them
(bool flowControl, int value) = CheckArgs(args);
if (!flowControl) return value;

// 2. Find the repo root
string root = RepoRoot();

// 3. Connection string per engine. The sa password comes from .env.
string cs = string.Empty;
DotNetEnv.Env.Load(Path.Combine(RepoRoot(), ".env"));
string? pass = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
if(args[2] == "postgres")
    cs = "Host=localhost;Port=55432;Database=payments_labs;Username=labs;Password=labs";
else
{
    // Fail here, not inside DbUp. A missing .env gives "Password=" and the driver then
    // says "login failed for user 'sa'", which sends you hunting the password you set
    // rather than the file you never copied.
    if (string.IsNullOrWhiteSpace(pass))
    {
        Console.WriteLine("Error: MSSQL_SA_PASSWORD is not set. Copy .env.example to .env and fill it in.");
        return 2;
    }
    cs = $"Server=localhost,51433;Database=PaymentsLabs;User Id=sa;Password={pass};TrustServerCertificate=true";
}

// 4. Make the database if it is missing.
if (args[2] == "postgres")
    EnsureDatabase.For.PostgresqlDatabase(cs);
else
    EnsureDatabase.For.SqlDatabase(cs);

// 5. Build the upgrader. Same four calls for both engines, only the first line differs.
string scriptDir = Path.Combine(root, "db", args[2]);
CheckScriptDir(scriptDir);

// 6. Run it.
var upgrader = Build(args[2], cs, scriptDir);
if (args[0] == "migrate")
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
        // Do not index args here: args.Length < 3 is one of the ways we get in, so
        // args[0] and args[1] may not exist. Join prints whatever was really typed.
        Console.WriteLine($"Error: expected 'migrate --engine <postgres|sqlserver>', got '{string.Join(' ', args)}'");
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