namespace KromicCommerce.Contracts.Catalog;

public sealed record AttributeValueDto(Guid Id, string Value, int SortOrder);

public sealed record ProductAttributeDto(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<AttributeValueDto> Values);
