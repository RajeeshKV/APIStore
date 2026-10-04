namespace KromicCommerce.Domain.Support;

/// <summary>Admin-assigned urgency. Customers never set priority themselves on creation.</summary>
public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}