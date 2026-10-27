namespace OrderProcessing.Shared.Exceptions;

public sealed class ShippingProviderUnavailableException : Exception
{
    public ShippingProviderUnavailableException(string message)
        : base(message)
    {
    }
}
