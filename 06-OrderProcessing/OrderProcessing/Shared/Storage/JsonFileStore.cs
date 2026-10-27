using System.Collections.Concurrent;
using System.Text.Json;
using OrderProcessing.Shared.Exceptions;

namespace OrderProcessing.Shared.Storage;

public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _directory;

    public JsonFileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task<List<T>> ReadAllAsync<T>(
        string fileName,
        CancellationToken ct = default)
    {
        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        await gate.WaitAsync(ct);
        try
        {
            return await ReadCoreAsync<T>(path, fileName, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteAllAsync<T>(
        string fileName,
        List<T> records,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        await gate.WaitAsync(ct);
        try
        {
            await WriteCoreAsync(path, records, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<List<T>> UpdateAsync<T>(
        string fileName,
        Func<List<T>, List<T>> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        await gate.WaitAsync(ct);
        try
        {
            // Keep reading, modifying, and writing under one lock.
            // Separate ReadAllAsync and WriteAllAsync calls could lose updates.
            List<T> current = await ReadCoreAsync<T>(path, fileName, ct);
            ct.ThrowIfCancellationRequested();

            List<T> updated = update(current)
                ?? throw new InvalidOperationException(
                    "The update callback returned null.");

            ct.ThrowIfCancellationRequested();
            await WriteCoreAsync(path, updated, ct);

            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    // Synchronous methods are reserved for the assignment's comparison versions.
    public List<T> ReadAll<T>(string fileName)
    {
        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        gate.Wait();
        try
        {
            return ReadCore<T>(path, fileName);
        }
        finally
        {
            gate.Release();
        }
    }

    public void WriteAll<T>(string fileName, List<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        gate.Wait();
        try
        {
            WriteCore(path, records);
        }
        finally
        {
            gate.Release();
        }
    }

    public List<T> Update<T>(
        string fileName,
        Func<List<T>, List<T>> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        string path = GetPath(fileName);
        SemaphoreSlim gate = GetLock(path);

        gate.Wait();
        try
        {
            List<T> current = ReadCore<T>(path, fileName);
            List<T> updated = update(current)
                ?? throw new InvalidOperationException(
                    "The update callback returned null.");

            WriteCore(path, updated);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Only allow a file name, not a path supplied by a caller.
        if (!string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only file names are allowed.",
                nameof(fileName));
        }

        return Path.Combine(_directory, fileName);
    }

    private static SemaphoreSlim GetLock(string path)
    {
        return FileLocks.GetOrAdd(
            Path.GetFullPath(path),
            static _ => new SemaphoreSlim(1, 1));
    }

    private static async Task<List<T>> ReadCoreAsync<T>(
        string path,
        string fileName,
        CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            byte[] json = await File.ReadAllBytesAsync(path, ct);

            return JsonSerializer.Deserialize<List<T>>(
                       json,
                       JsonDefaults.Options)
                   ?? throw new JsonException(
                       "Expected a JSON array, not null.");
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex);
        }
    }

    private static List<T> ReadCore<T>(string path, string fileName)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            byte[] json = File.ReadAllBytes(path);

            return JsonSerializer.Deserialize<List<T>>(
                       json,
                       JsonDefaults.Options)
                   ?? throw new JsonException(
                       "Expected a JSON array, not null.");
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex);
        }
    }

    private static async Task WriteCoreAsync<T>(
        string path,
        List<T> records,
        CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            records,
            JsonDefaults.Options);

        await WriteAtomicallyAsync(path, json, ct);
    }

    private static void WriteCore<T>(string path, List<T> records)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            records,
            JsonDefaults.Options);

        WriteAtomically(path, json);
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] json,
        CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The target directory could not be resolved.");

        Directory.CreateDirectory(directory);

        // Write to a temporary file first; the existing JSON stays untouched
        // until the complete replacement file is ready.
        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(json, ct);
                await stream.FlushAsync(ct);
            }

            ct.ThrowIfCancellationRequested();

            // Rename within the same directory to replace the target atomically.
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // A cancelled or failed write must not leave temporary files behind.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void WriteAtomically(string path, byte[] json)
    {
        string? directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The target directory could not be resolved.");

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
