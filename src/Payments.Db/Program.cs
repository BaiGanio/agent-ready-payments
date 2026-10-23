using Payments.Db;
using Payments.Migrations;

// 1. Read the args and parse them.
(bool flowControl, int value) = Helper.CheckArgs(args);
if (!flowControl) return value;

string engine = args[2];

// 2. Connection string per engine. The sa password comes from .env.
//    A missing password throws, so catch it here: the console is the one place
//    that speaks exit codes, and 2 means "you typed or configured it wrong".
string cs;
try
{
    cs = engine == "postgres" ? LocalDatabases.Postgres() : LocalDatabases.SqlServer();
}
catch (ArgumentException ex)
{
    Console.WriteLine(ex.Message);
    return 2;
}

// 3. Make the database if it is missing, then run the waiting scripts.
Migrator.EnsureDatabase(engine, cs);

var result = Migrator.Run(engine, cs);
if (!result.Successful)
{
    Console.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("All good!");
return 0; // Success


