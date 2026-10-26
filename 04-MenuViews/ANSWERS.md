# Assignment 04 — Menu Views

Student name: [Your Name]
Group / ID: [Your Group / ID]

## What is wrong

The original implementation uses Views++ while eight threads update the same counter concurrently. The increment is not atomic, so updates can be lost. In the baseline run, the expected count was 400000, but the actual count was 142264.

## How you fixed it

I replaced the shared counter field with a private integer and used Interlocked.Increment to update it atomically. The Views property uses Volatile.Read to read the latest counter value safely.

This prevents lost increments without requiring a manually managed lock.

## Verification

The project was built and run three times. Each run must report:

Expected: 400000
Actual:   400000

## When would you protect this update differently?

For a single counter, Interlocked.Increment is a simple and efficient choice.

If an update involves multiple shared fields or several operations that must stay consistent together, I would use lock to protect the entire critical section.
