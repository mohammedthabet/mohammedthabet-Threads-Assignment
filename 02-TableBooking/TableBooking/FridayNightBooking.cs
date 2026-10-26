namespace TableBooking;

public sealed class FridayNightBooking
{
    private readonly object _bookingLock = new();

    public int TablesLeft { get; private set; } = 5;
    public int Booked { get; private set; }

    public void Book(string customerName)
    {
        // Simulate the delay before attempting to book a table.
        Thread.Sleep(20);

        lock (_bookingLock)
        {
            // Keep the availability check and counter updates atomic
            // to prevent concurrent customers from overbooking.
            if (TablesLeft <= 0)
                return;

            TablesLeft--;
            Booked++;

            Console.WriteLine(
                $"{customerName} booked a table. Left={TablesLeft}");
        }
    }
}