# Order Processing System

## Overview

A C# console application demonstrating synchronous and asynchronous order validation, concurrent order processing, checkout workflows, and scalable processing using .NET 10.

## Requirements

- .NET 10 SDK
- Windows, Linux, or macOS with .NET 10 support

## Build

Run from the repository root:

```powershell
dotnet build .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj
```

## Run the Phases

```powershell
dotnet run --project .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj -- phase1
dotnet run --project .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj -- phase2
dotnet run --project .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj -- phase3
dotnet run --project .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj -- phase4
```

Run the race-condition demonstration:

```powershell
dotnet run --project .\06-OrderProcessing\OrderProcessing\OrderProcessing.csproj -- race-test
```

## Verified Results

### Phase 1 — Synchronous vs. Asynchronous

- Incoming orders: 20
- Validated: 15
- Rejected: 5
- Validation results matched the expected baseline.
- A separate timing run measured 35.5 seconds for synchronous validation and 53.6 seconds for asynchronous validation plus sequential persistence. These timings include different amounts of work and should not be treated as a direct like-for-like performance comparison.

### Phase 2 — Concurrent Order Processing

- Incoming orders: 20
- Completed: 12
- Rejected: 6
- Failed: 2
- Both failures were caused by the simulated shipping provider being unavailable.

### Phase 3 — Full Checkout Workflow

- Orders saved: 20
- Completed: 11
- Rejected: 6
- Payment failed: 2
- Failed after payment: 1
- Payments: 13; captured: 11; declined: 1; refunded: 1
- Emails sent: 10
- Emails queued for retry: 1

The results matched the expected baseline of 20 orders.

### Phase 4 — Scale-Up

A recorded verification run reported:

- Flagged records: 1,005
- Sequential processing: 11,536 ms
- Parallel processing: 2,065 ms
- Measured speed-up: 5.59x
- Email sending concurrency limit: 4
- Emails sent: 30
- Validation: PASS

Performance figures are representative measurements from a specific run, not guaranteed timings.

### Race-Condition Demonstration

- Unsafe reservation: 2 successful reservations against stock of 1.
- Safe reservation: 1 successful reservation against stock of 1.
- The demonstration reported PASS.

## Screenshots and Sample Output

The `screenshots` directory contains the supplied screenshots.

The `sample-output` directory contains the project's sample output files. Keep these files separate from runtime-generated output.

## Notes

- Processing durations vary because the application simulates database and external-service latency.
- Shipping failures, payment failures, and email retries are simulated scenarios.
- The recorded results document observed runs; they do not guarantee identical timings on every machine.
