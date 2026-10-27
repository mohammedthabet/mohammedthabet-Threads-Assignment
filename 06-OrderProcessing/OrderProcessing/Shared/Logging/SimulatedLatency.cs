namespace OrderProcessing.Shared.Logging;

public static class SimulatedLatency
{
    public static async Task DatabaseQueryAsync(
        string source,
        string operation,
        CancellationToken ct = default)
    {
        Log.Info(source, $"{operation}: Opening connection...");
        await Task.Delay(200, ct);

        int delay = Random.Shared.Next(300, 601);
        Log.Info(source, $"{operation}: Executing query...");
        await Task.Delay(delay, ct);
    }

    public static async Task DatabaseWriteAsync(
        string source,
        string operation,
        CancellationToken ct = default)
    {
        Log.Info(source, $"{operation}: Opening connection...");
        await Task.Delay(200, ct);

        Log.Info(source, $"{operation}: Beginning transaction...");
        await Task.Delay(100, ct);

        Log.Info(source, $"{operation}: Inserting/updating...");
        await Task.Delay(400, ct);
    }

    public static async Task CommitAsync(
        string source,
        CancellationToken ct = default)
    {
        Log.Info(source, "Committing transaction...");
        await Task.Delay(200, ct);
    }

    public static async Task ExternalApiAsync(
        string source,
        string operation,
        CancellationToken ct = default)
    {
        Log.Info(source, $"{operation}: DNS/handshake...");
        await Task.Delay(150, ct);

        Log.Info(source, $"{operation}: Sending request...");
        await Task.Delay(300, ct);

        int delay = Random.Shared.Next(500, 1501);
        Log.Info(source, $"{operation}: Waiting for response...");
        await Task.Delay(delay, ct);
    }

    public static async Task PaymentGatewayAsync(
        string source,
        CancellationToken ct = default)
    {
        Log.Info(source, "Authorizing payment...");
        await Task.Delay(800, ct);

        Log.Info(source, "Running fraud check...");
        await Task.Delay(500, ct);

        Log.Info(source, "Capturing payment...");
        await Task.Delay(700, ct);
    }

    public static async Task SendEmailAsync(
        string source,
        CancellationToken ct = default)
    {
        Log.Info(source, "Rendering email template...");
        await Task.Delay(200, ct);

        Log.Info(source, "Connecting to SMTP...");
        await Task.Delay(300, ct);

        Log.Info(source, "Sending email...");
        await Task.Delay(500, ct);
    }
}
