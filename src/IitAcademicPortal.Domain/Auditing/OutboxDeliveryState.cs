namespace IitAcademicPortal.Domain.Auditing;

public enum OutboxDeliveryState
{
    Pending,
    RetryScheduled,
    Exhausted,
    Delivered,
    Handled,
}
