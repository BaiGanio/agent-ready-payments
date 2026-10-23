namespace Payments.Db;

public static class Helper
{
    public static (bool flowControl, int value) CheckArgs(string[] args)
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
            return (flowControl: false, value: 2);
        }

        return (flowControl: true, value: default);
    }
}