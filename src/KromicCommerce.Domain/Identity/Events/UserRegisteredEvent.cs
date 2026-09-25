namespace KromicCommerce.Domain.Identity.Events;

public sealed class UserRegisteredEvent(Guid userId, string email, UserRole role) : DomainEvent
{
    public Guid UserId { get; } = userId;
    public string Email { get; } = email;
    public UserRole Role { get; } = role;
}
