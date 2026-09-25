namespace KromicCommerce.Application.Options;

/// <summary>Bootstrap secret consumed by BootstrapAdminHandler.</summary>
public sealed class BootstrapOptions
{
    public string BootstrapSecret { get; set; } = string.Empty;
}
