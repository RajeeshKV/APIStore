namespace KromicCommerce.Application.Features.Catalog.Products.ChangeProductStatus;

public sealed record PublishProductCommand(Guid ProductId) : ICommand;
public sealed record ArchiveProductCommand(Guid ProductId) : ICommand;
public sealed record UnpublishProductCommand(Guid ProductId) : ICommand;
