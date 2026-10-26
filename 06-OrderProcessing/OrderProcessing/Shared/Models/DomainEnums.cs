namespace OrderProcessing.Shared.Models;

// These enums keep statuses explicit and readable in the JSON files.
public enum LoyaltyTier
{
    Bronze,
    Silver,
    Gold
}

public enum OrderStatus
{
    Pending,
    Validated,
    Rejected,
    PaymentFailed,
    Completed,
    Failed
}

public enum PaymentStatus
{
    Captured,
    Declined,
    Refunded
}
