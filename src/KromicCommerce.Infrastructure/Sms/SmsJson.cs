using System.Text.Encodings.Web;
using System.Text.Json;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Shared JSON settings for provider request bodies.
/// </summary>
/// <remarks>
/// The default encoder escapes <c>+</c> as <c>\u002B</c>. That is valid JSON, but it makes
/// phone numbers unreadable in gateway debug logs and some strict SMS gateways reject the
/// literal escape. The relaxed encoder emits <c>+</c> as-is, which is safe here because these
/// bodies are sent as an HTTP request, never embedded in HTML.
/// </remarks>
internal static class SmsJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
