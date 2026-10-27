using System.Diagnostics;

namespace InvoiceExport;

public sealed class InvoiceExportService
{
    private readonly OrderDatabase _db;
    private readonly CustomerFileStore _customers;
    private readonly InvoiceFileWriter _invoices;

    public InvoiceExportService(
        OrderDatabase db,
        CustomerFileStore customers,
        InvoiceFileWriter invoices)
    {
        _db = db;
        _customers = customers;
        _invoices = invoices;
    }

    public async Task ExportAllPendingAsync(
        CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();

        List<Order> orders = await _db.GetPendingOrdersAsync(ct);

        var tasks = new List<Task<string>>(orders.Count);

        foreach (Order order in orders)
        {
            tasks.Add(ExportOneAsync(order, ct));
        }

        foreach (Task<string> task in tasks)
        {
            string path = await task;
            Console.WriteLine($"Wrote {path}");
        }

        Console.WriteLine(
            $"Export finished in {watch.Elapsed.TotalSeconds:F1} s");
    }

    private async Task<string> ExportOneAsync(
        Order order,
        CancellationToken ct)
    {
        Order fresh = await _db.GetOrderByIdAsync(order.Id, ct)
                      ?? throw new InvalidOperationException(
                          $"Order {order.Id} missing");

        CustomerProfile customer =
            await _customers.ReadProfileAsync(fresh.CustomerId, ct);

        return await _invoices.WriteInvoiceAsync(
            fresh,
            customer,
            ct);
    }
}