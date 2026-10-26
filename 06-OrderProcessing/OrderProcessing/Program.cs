using OrderProcessing.Phase1;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "phase1", StringComparison.OrdinalIgnoreCase))
        {
            await new Phase1Runner().RunAsync();
            return;
        }

        Console.WriteLine("Order Processing System");
        Console.WriteLine("1. Run Phase 1 (Sync vs Async)");
        Console.WriteLine("0. Exit");
        Console.Write("Select an option: ");

        string? choice = Console.ReadLine();

        if (choice == "1")
            await new Phase1Runner().RunAsync();
    }
}
