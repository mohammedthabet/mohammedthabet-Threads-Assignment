using OrderProcessing.Phase1;
using OrderProcessing.Phase2;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "phase1":
                    await new Phase1Runner().RunAsync();
                    return;

                case "phase2":
                    await new Phase2Runner().RunAsync();
                    return;
            }
        }

        Console.WriteLine("Order Processing System");
        Console.WriteLine("1. Phase 1 - Sync vs Async");
        Console.WriteLine("2. Phase 2 - Concurrent Processing");
        Console.WriteLine("0. Exit");
        Console.Write("Select an option: ");

        switch (Console.ReadLine())
        {
            case "1":
                await new Phase1Runner().RunAsync();
                break;

            case "2":
                await new Phase2Runner().RunAsync();
                break;
        }
    }
}
