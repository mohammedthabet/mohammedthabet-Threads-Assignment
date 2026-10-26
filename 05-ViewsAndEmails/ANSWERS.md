# Assignment 05 — Views and emails

## Failures you observed

The original Dictionary was accessed concurrently without synchronization. Runs produced an InvalidOperationException, a duplicate-key ArgumentException, and an incorrect view count (19698 instead of 50000).

The email queue was also accessed concurrently. Checking Count and calling Dequeue were separate operations, so multiple workers could interfere with each other.

## What you changed

I used a lock to protect the complete view-count update, including checking whether a product exists, initializing it, and incrementing its count.

I also protected queue operations with a separate lock. Each worker now checks the queue and removes a job inside the same critical section. Workers exit when the producers are done and the queue is empty.

The existing lock around SentEmails was retained.

## Collection safety vs business-rule safety

A thread-safe collection protects its own internal operations, but it does not automatically make a sequence of business operations atomic.

For example, checking whether a dictionary key exists and then adding or incrementing it requires synchronization across the whole sequence. Likewise, checking a queue count and then removing a job must be coordinated so two workers cannot make conflicting decisions.
