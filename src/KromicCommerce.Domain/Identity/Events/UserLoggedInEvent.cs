namespace KromicCommerce.Domain.Identity.Events;

public sealed class UserLoggedInEvent(Guid userId, string email) : DomainEvent
{
    public Guid UserId { get; } = userId;
    public string Email { get; } = email;
}
