namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Application roles. Persisted as strings (enforced via EF ConfigureConventions).
/// Keep this minimal — do not add roles for ceremony.
/// </summary>
public enum UserRole
{
    Customer,
    Admin
}
