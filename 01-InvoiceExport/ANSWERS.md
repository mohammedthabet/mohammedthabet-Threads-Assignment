# Assignment 01 — Invoice Export




## 1. Problems in the Original Code

- The export process handled orders sequentially, so each order had to finish before the next one started.
- `Thread.Sleep` blocked the current thread during simulated database and file operations.
- Synchronous file access added more blocking time to the export process.

## 2. Changes Made

- Replaced the synchronous database methods with asynchronous methods returning `Task<T>`.
- Replaced `Thread.Sleep` with `await Task.Delay` for simulated latency.
- Changed customer profile reading and invoice writing to asynchronous file operations.
- Updated the export service to start each order-processing task before awaiting the tasks individually.
- Updated the entry point to await the asynchronous export operation.

These changes preserve the original business flow and invoice content while allowing waiting periods from different orders to overlap.

## 3. Console Output Before the Fix

```text
Starting invoice export...
Export finished in 6.0 s
```

Five invoice files were generated, from `invoice-1001.txt` to `invoice-1005.txt`.

## 4. Console Output After the Fix

```text
Starting invoice export...
Export finished in 1.9 s
```

Five invoice files were generated successfully, from `invoice-1001.txt` to `invoice-1005.txt`.

## 5. Verification

- Original execution time: 6.0 seconds.
- Updated execution time: 1.9 seconds.
- Approximate execution-time reduction: 68.3%.
- Invoice content comparison: all five files matched the original output.
- Build: successful.
- Runtime errors: none observed during the successful run.

## 6. Conclusion

The asynchronous implementation reduced the total export time while preserving the original invoice content and business flow.