using System.Net;
using System.Text;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// Records every request an adapter makes, in order, and returns a canned response.
/// </summary>
internal sealed class RecordingHttpMessageHandler(
    HttpStatusCode status,
    string body,
    string contentType = "application/json") : HttpMessageHandler
{
    public RecordingHttpMessageHandler(Exception exception) : this(
        HttpStatusCode.InternalServerError, string.Empty) => _throws = exception;

    private readonly Exception? _throws;
    private readonly Queue<(HttpStatusCode Status, string Body)> _sequence = new();

    /// <summary>
    /// Queues the response for the second and subsequent requests. The first request always
    /// gets the canned status/body passed to the constructor.
    /// </summary>
    public RecordingHttpMessageHandler Then(
        HttpStatusCode nextStatus, string nextBody)
    {
        _sequence.Enqueue((nextStatus, nextBody));
        return this;
    }

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.ToString(),
            request.RequestUri.PathAndQuery,
            content,
            request.Headers.ToDictionary(
                h => h.Key,
                h => string.Join(",", h.Value),
                StringComparer.OrdinalIgnoreCase)));

        if (_throws is not null)
            throw _throws;

        var response = Requests.Count == 1
            ? (Status: status, Body: body)
            : _sequence.Count > 0
                ? _sequence.Dequeue()
                : (Status: status, Body: body);

        return new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, contentType)
        };
    }
}

/// <summary>One captured outbound request.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    string Uri,
    string PathAndQuery,
    string Body,
    IReadOnlyDictionary<string, string> Headers);
