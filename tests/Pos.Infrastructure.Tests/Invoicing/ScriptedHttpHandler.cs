using System.Net;

namespace Pos.Infrastructure.Tests.Invoicing;

/// <summary>HttpMessageHandler kịch bản hóa để test adapter HĐĐT không cần server thật.</summary>
public sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public List<(string Method, string Path, string Body)> Calls { get; } = new();

    public ScriptedHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
        return _responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
