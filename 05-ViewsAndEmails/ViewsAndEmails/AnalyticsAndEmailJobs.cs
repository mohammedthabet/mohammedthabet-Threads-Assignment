namespace ViewsAndEmails;

public sealed class AnalyticsAndEmailJobs
{
    private readonly Dictionary<string, int> _views = new();
    private readonly object _viewsSync = new();

    private readonly Queue<string> _emailJobs = new();
    private readonly object _emailSync = new();

    public readonly List<string> SentEmails = new();

    private readonly object _sentSync = new();

    public volatile bool ProducersDone;

    public void RecordView(string product)
    {
        lock (_viewsSync)
        {
            // Keep the lookup, initialization, and increment in one critical section.
            if (!_views.ContainsKey(product))
                _views.Add(product, 0);

            _views[product]++;
        }
    }

    public int GetViewCount(string product)
    {
        lock (_viewsSync)
        {
            return _views.TryGetValue(product, out int count) ? count : 0;
        }
    }

    public void PlaceOrder(int orderId)
    {
        lock (_emailSync)
        {
            // Synchronize queue access so workers cannot race with producers.
            _emailJobs.Enqueue($"Email: order {orderId}");
        }
    }

    public void EmailWorker()
    {
        while (true)
        {
            string? job;

            lock (_emailSync)
            {
                // Checking the queue and removing a job must happen together.
                if (_emailJobs.Count > 0)
                {
                    job = _emailJobs.Dequeue();
                }
                else if (ProducersDone)
                {
                    return;
                }
                else
                {
                    job = null;
                }
            }

            if (job is not null)
            {
                lock (_sentSync)
                {
                    SentEmails.Add(job);
                }

                continue;
            }

            Thread.Sleep(1);
        }
    }
}
