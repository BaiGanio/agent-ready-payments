using DbUp;
using DbUp.Engine;

namespace Payments.Migrations
{
    /// <summary>Makes the database and runs the .sql scripts for one engine. The only DbUp caller.</summary>
    public static class Migrator
    {
        /// <summary>Creates the database if it is missing. Safe to call again.</summary>
        public static void EnsureDatabase(string engine, string connectionString)
        {
            if (engine == "postgres")
                DbUp.EnsureDatabase.For.PostgresqlDatabase(connectionString);
            else
                DbUp.EnsureDatabase.For.SqlDatabase(connectionString);
        }

        /// <summary>
        /// Runs every script in db/&lt;engine&gt; that has not run yet, one transaction per script.
        /// </summary>
        public static DatabaseUpgradeResult Run(string engine, string connectionString, string? scriptDir = null)
        {
            scriptDir ??= Path.Combine(RepoRoot.Find(), "db", engine);

            // Fail here, not inside DbUp. An empty folder is a "success" with 0 scripts, which
            // reads the same as "already up to date" and hides a wrong path for hours.
            if (!Directory.Exists(scriptDir))
                throw new DirectoryNotFoundException($"No script folder: {scriptDir}");

            if (Directory.GetFiles(scriptDir, "*.sql").Length == 0)
                throw new InvalidOperationException($"No .sql scripts in: {scriptDir}");

            var builder = engine == "postgres"
                ? DeployChanges.To.PostgresqlDatabase(connectionString)
                : DeployChanges.To.SqlDatabase(connectionString);

            return builder
                .WithScriptsFromFileSystem(scriptDir)
                .WithTransactionPerScript()
                .LogToConsole()
                .Build()
                .PerformUpgrade();
        }
    }
}
