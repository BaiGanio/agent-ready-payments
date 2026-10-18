internal readonly record struct MigrationOutcome(bool Ok, string? Error)
{
    public int ExitCode => Ok ? 0 : 1;
}