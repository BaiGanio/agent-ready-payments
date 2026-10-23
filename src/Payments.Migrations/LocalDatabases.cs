namespace Payments.Migrations
{
    public static class LocalDatabases
    {
        public static string Postgres(string database = "payments_labs")
        {
            return $"Host=localhost;Port=55432;Database={database};Username=labs;Password=labs";
        }

        public static string SqlServer(string database = "PaymentsLabs")
        {
            DotNetEnv.Env.Load(Path.Combine(RepoRoot.Find(), ".env"));
            string? pass = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
            
            if (string.IsNullOrWhiteSpace(pass)) 
                throw new ArgumentException("Error: MSSQL_SA_PASSWORD is not set. Copy .env.example to .env and fill it in.");
            
            return $"Server=localhost,51433;Database={database};User Id=sa;Password={pass};TrustServerCertificate=true";
        }
    }
}