using System.Net;
using System.Text;
using System.Text.Json;
using Chat.Client.Models;
using Chat.Client.Services;
using Xunit;

namespace Chat.Client.Tests;

public sealed class ChatApiTests
{
    [Fact]
    public async Task HistoryAndSyncUseDistinctCursorsAndDeserializeContractDto()
    {
        var id = Guid.NewGuid();
        var calls = new List<Uri>();
        var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            return Json(HttpStatusCode.OK, new[] { new MessageResponse(Guid.NewGuid(), id, Guid.NewGuid(), 42, "Xin chào", DateTimeOffset.UtcNow) });
        });
        var api = new MessagesApiClient(new ApiRequestSender(new HttpClient(handler) { BaseAddress = new("http://localhost:5080/") }));
        var latest = await api.GetLatestAsync(id);
        await api.GetOlderAsync(id, 42);
        await api.GetNewAsync(id, 42);
        Assert.Equal(42, Assert.Single(latest).Seq);
        Assert.DoesNotContain("afterSeq", calls[0].Query);
        Assert.DoesNotContain("beforeSeq", calls[0].Query);
        Assert.Contains("beforeSeq=42", calls[1].Query);
        Assert.DoesNotContain("afterSeq", calls[1].Query);
        Assert.Contains("afterSeq=42", calls[2].Query);
        Assert.DoesNotContain("beforeSeq", calls[2].Query);
    }

    [Fact]
    public async Task SendingUsesSharedCsrfAndPreservesIdAndBody()
    {
        var id = Guid.NewGuid();
        var draft = new SendMessageRequest(Guid.NewGuid(), "  giữ nguyên\n");
        var tokensFetched = 0;
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/csrf")
            {
                tokensFetched++;
                return Json(HttpStatusCode.OK, new { token = "test-csrf" });
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-csrf", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var decoded = JsonSerializer.Deserialize<SendMessageRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(draft, decoded);
            return Json(HttpStatusCode.Created, new MessageResponse(Guid.NewGuid(), id, Guid.NewGuid(), 1, draft.Body, DateTimeOffset.UtcNow));
        });
        var api = new MessagesApiClient(new ApiRequestSender(new HttpClient(handler) { BaseAddress = new("http://localhost:5080/") }));
        Assert.Equal(draft.Body, (await api.SendAsync(id, draft)).Body);
        Assert.Equal(1, tokensFetched);
    }

    [Fact]
    public async Task BackendErrorCodeAndStatusReachChatWithoutBeingReplaced()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.Forbidden, new { code = "CONVERSATION_ACCESS_DENIED", message = "Access denied" }));
        var api = new MessagesApiClient(new ApiRequestSender(new HttpClient(handler) { BaseAddress = new("http://localhost:5080/") }));
        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetLatestAsync(Guid.NewGuid()));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Code);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
    };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
