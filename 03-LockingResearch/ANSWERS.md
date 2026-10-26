# Assignment 03 — Locking Research


## Q1. What is System.Threading.Monitor used for?

Monitor is used to synchronize access to shared data. It allows only one thread at a time to enter a critical section protected by the same lock object. This helps prevent race conditions when multiple threads read or modify shared state.

## Q2. What does the C# lock statement compile to?

For a normal reference-type lock object the compiler generates code using Monitor.Enter and Monitor.Exit inside a try/finally structure. This ensures that the lock is released even if an exception occurs. C# 13 and later use a different optimized mechanism when the lock object is of type System.Threading.Lock.

## Q3. List at least two differences between lock and direct Monitor APIs.

1. The lock statement has simpler syntax and releases the lock automatically. Direct Monitor calls require careful handling of Monitor.Exit, usually inside a finally block.
2. Monitor.TryEnter allows a thread to attempt to acquire a lock with a timeout.
3. Monitor.Wait and Monitor Pulse support coordination between threads based on changes in shared state.

## Q4. When might a developer use Monitor.TryEnter, Monitor.Pulse, or Monitor.Wait instead of a plain lock?

- TryEnter is useful when a thread should wait only for a limited time to acquire a lock.
- Wait allows a thread to release the lock and wait for a state change.
- Pulse signals one waiting thread after the shared state changes.

These methods are useful when synchronization requires more control than simple mutual exclusion. After Wait returns, the thread must reacquire the lock and check the condition again.

## Q5. Write the same critical section twice.

Using lock:

```csharp
private readonly object _sync = new();
private int _count;

public void Increment()
{
    lock (_sync)
    {
        _count++;
    }
}
```

Using Monitor.Enter and Monitor.Exit:

```csharp
private readonly object _sync = new();
private int _count;

public void Increment()
{
    bool lockTaken = false;

    try
    {
        Monitor.Enter(_sync, ref lockTaken);
        _count++;
    }
    finally
    {
        if (lockTaken)
        {
            Monitor.Exit(_sync);
        }
    }
}
```

Both versions protect the same shared counter. The lock statement is simpler while the Monitor API provides more direct control over acquiring and releasing the lock.

## References

1. https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor
2. https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
