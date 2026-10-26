using System.Threading;

namespace MenuViews;

public sealed class MenuViewCounter
{
    private int _views;

    public int Views => Volatile.Read(ref _views);

    public void RegisterView()
    {
        // Atomically increment the shared counter to prevent lost updates.
        Interlocked.Increment(ref _views);
    }
}
