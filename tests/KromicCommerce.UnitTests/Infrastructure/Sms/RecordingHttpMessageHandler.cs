using System.Net;
using System.Text;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// Records <b>every</b> request an adapter makes, in order, and returns a canned response.
/// </summary>
/// <remarks>
/// The existing <see cref="StubHttpMessageHandler"/> keeps only the last request, which is enough
/// for a single-route provider but cannot express the behaviour these adapters now have: attempting
/// a native OTP route and then deciding whether to fall back to the transactional one. Assertions
/// like "exactly one request was made" and "the fallback went to this URL" need the whole sequence.
/// </remarks>
internal sealed class RecordingHttpMessageHandler(
    HttpStatusCode status,
    string body,
    string contentType = "application/json") : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _sequence = new();

    /// <summary>
    /// Queues the response for the <b>second and subsequent</b> requests. The first request always
    /// gets the canned status/body passed to the constructor, so a "native attempt fails, fallback
    /// succeeds" scenario reads as <c>new Handler(NotFound, errorBody).Then(OK, okBody)</c>.
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
            // The escaped path, which is what actually goes on the wire. Uri.ToString() unescapes,
            // so it cannot show whether a path segment was correctly percent-encoded.
            request.RequestUri.PathAndQuery,
            content,
            request.Headers.ToDictionary(
                h => h.Key,
                h => string.Join(",", h.Value),
                StringComparer.OrdinalIgnoreCase)));

        // Named on both arms, otherwise the ternary yields an unnamed tuple and the element names
        // are lost.
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
