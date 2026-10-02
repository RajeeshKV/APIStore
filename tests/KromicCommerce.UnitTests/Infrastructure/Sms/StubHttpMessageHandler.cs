using System.Net;
using System.Text;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// Captures the outgoing request and returns a canned response, so provider adapters can be
/// asserted on their exact wire contract without a network call.
/// </summary>
internal sealed class StubHttpMessageHandler(
    HttpStatusCode status,
    string body,
    string contentType = "application/json") : HttpMessageHandler
{
    public StubHttpMessageHandler(Exception exception) : this(
        HttpStatusCode.InternalServerError, string.Empty) => _throws = exception;

    private readonly Exception? _throws;

    public HttpRequestMessage? Request { get; private set; }

    public string? RequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;

        RequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throws is not null)
            throw _throws;

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
