using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public sealed class InvoiceGenerator
{
    public async Task GenerateAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        long checksum = await Task.Run(() =>
        {
            long result = 0;

            for (int i = 0; i < 20_000_000; i++)
            {
                if ((i & 4095) == 0)
                    ct.ThrowIfCancellationRequested();

                result += i % 97;
            }

            return result;
        }, ct);

        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "invoices");

        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            $"invoice_{order.Id}.txt");

        string content =
            $"Invoice #{order.Id}{Environment.NewLine}" +
            $"Customer ID: {order.CustomerId}{Environment.NewLine}" +
            $"Subtotal: {order.SubTotal:F2}{Environment.NewLine}" +
            $"Discount: {order.Discount:F2}{Environment.NewLine}" +
            $"Shipping: {order.ShippingCost:F2}{Environment.NewLine}" +
            $"Total: {order.TotalAmount:F2}{Environment.NewLine}" +
            $"Render checksum: {checksum}{Environment.NewLine}";

        await File.WriteAllTextAsync(path, content, ct);

        Log.Success(
            "InvoiceGenerator",
            $"Invoice generated for Order #{order.Id}.");
    }
}
