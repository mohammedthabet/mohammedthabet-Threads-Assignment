
# Assignment 02 — Answers


## What is wrong

The booking method has a race condition. Multiple threads can check the available tables before any of them updates the counters. This can lead to incorrect booking counts and a negative number of remaining tables.

## How you fixed it

I added a private lock object to protect the availability check and the booking counter updates. The check and updates now execute as one protected operation, so only one thread can modify the booking state at a time.

I also moved the simulated delay outside the critical section to avoid holding the lock while waiting. This preserves concurrent execution while preventing overbooking.

## Baseline Results

The original implementation produced inconsistent results across three runs:

| Run | Booked | Tables Left |
|---|---:|---:|
| 1 | 1 | 2 |
| 2 | 20 | -13 |
| 3 | 23 | -9 |

## Verification After the Fix

The updated implementation was built successfully and run three times.

```text
Booked=5, TablesLeft=0
Booked=5, TablesLeft=0
Booked=5, TablesLeft=0
```

All three runs produced the correct final booking counts.
