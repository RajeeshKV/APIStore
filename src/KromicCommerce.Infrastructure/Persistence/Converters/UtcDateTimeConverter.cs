using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KromicCommerce.Infrastructure.Persistence.Converters;

/// <summary>
/// EF Core value converter that ensures DateTime values read from the database
/// are always returned with DateTimeKind.Utc.
/// </summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
