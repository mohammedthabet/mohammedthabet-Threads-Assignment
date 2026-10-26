using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;
using OrderProcessing.Phase4;

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
                case "phase3":
                case "phase4":
                case "all":
                    using (var cts = new CancellationTokenSource())
                    {
                        Console.CancelKeyPress += (_, e) =>
                        {
                            e.Cancel = true;
                            cts.Cancel();
                        };
                        int cancelIndex = Array.FindIndex(args, a =>
                            a.Equals("--cancel-after", StringComparison.OrdinalIgnoreCase));
                        if (cancelIndex >= 0 && cancelIndex + 1 < args.Length &&
                            int.TryParse(args[cancelIndex + 1], out int cancelAfterMs) && cancelAfterMs > 0)
                            cts.CancelAfter(cancelAfterMs);

                        string mode = args[0].ToLowerInvariant();
                        if (mode == "all")
                        {
                            await new Phase1Runner().RunAsync();
                            await new Phase2Runner().RunAsync();
                            await new Phase3Runner().RunAsync(cts.Token);
                            cts.Token.ThrowIfCancellationRequested();
                            await new Phase4Runner().RunAsync(cts.Token);
                        }
                        else if (mode == "phase3")
                            await new Phase3Runner().RunAsync(cts.Token);
                        else
                        {
                            try
                            {
                                await new Phase4Runner().RunAsync(cts.Token);
                            }
                            catch (OperationCanceledException) when (cts.IsCancellationRequested)
                            {
                                Console.WriteLine("Phase execution cancelled.");
                            }
                        }
                    }
                    return;
            }
        }

        Console.WriteLine("Order Processing System");
        Console.WriteLine("1. Phase 1 - Sync vs Async");
        Console.WriteLine("2. Phase 2 - Concurrent Processing");
        Console.WriteLine("3. Phase 3 - Full Checkout Workflow");
        Console.WriteLine("4. Phase 4 - Scale-Up");
        Console.WriteLine("0. Exit");
        Console.Write("Select an option: ");
        switch (Console.ReadLine())
        {
            case "1": await new Phase1Runner().RunAsync(); break;
            case "2": await new Phase2Runner().RunAsync(); break;
            case "3": await new Phase3Runner().RunAsync(); break;
            case "4": await new Phase4Runner().RunAsync(); break;
        }
    }
}
