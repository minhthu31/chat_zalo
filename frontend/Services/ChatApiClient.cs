using System.Net.Http.Json;
using System.Text.Json;
using Chat.Client.Models.Auth;
using Chat.Client.Models.Conversations;
using Chat.Client.Models.Messages;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Chat.Client.Services;

/// <summary>HTTP adapter for the v1.2 API. Cookie credentials and CSRF are centralized here.</summary>
public sealed class ChatApiClient(HttpClient http)
{
    private string? _csrfToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> RefreshCsrfAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/csrf");
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("API không trả CSRF token.");
        _csrfToken = payload.Token;
        return _csrfToken;
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/auth/me", null, false, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonOptions, ct))!;
    }

    public async Task<IReadOnlyList<ConversationDto>> GetConversationsAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/conversations", null, false, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ConversationDto>>(JsonOptions, ct) ?? [];
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(
        Guid conversationId, long? afterSeq = null, long? beforeSeq = null, int limit = 20,
        CancellationToken ct = default)
    {
        if (afterSeq.HasValue && beforeSeq.HasValue)
            throw new ArgumentException("Không được truyền đồng thời afterSeq và beforeSeq.");
        var query = $"?limit={Math.Clamp(limit, 1, 100)}";
        if (afterSeq.HasValue) query += $"&afterSeq={afterSeq.Value}";
        if (beforeSeq.HasValue) query += $"&beforeSeq={beforeSeq.Value}";
        using var response = await SendAsync(HttpMethod.Get,
            $"api/conversations/{conversationId}/messages{query}", null, false, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MessageResponse>>(JsonOptions, ct) ?? [];
    }

    public async Task<MessageResponse> SendMessageAsync(
        Guid conversationId, string body, Guid clientMessageId, CancellationToken ct = default)
    {
        var request = new SendMessageRequest(clientMessageId, body);
        using var response = await SendAsync(HttpMethod.Post,
            $"api/conversations/{conversationId}/messages", request, true, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct))
            ?? throw new InvalidOperationException("API không trả MessageResponse.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string uri, object? body, bool needsCsrf, CancellationToken ct)
    {
        if (needsCsrf && string.IsNullOrWhiteSpace(_csrfToken))
            await RefreshCsrfAsync(ct);

        var request = new HttpRequestMessage(method, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        if (needsCsrf) request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);
        var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
            needsCsrf && _csrfToken is not null)
        {
            // Token may have rotated after login. Refresh once and retry the same idempotent request.
            var code = await ReadErrorCodeAsync(response, ct);
            response.Dispose();
            if (code == "INVALID_CSRF_TOKEN")
            {
                await RefreshCsrfAsync(ct);
                using var retry = new HttpRequestMessage(method, uri);
                retry.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                if (body is not null) retry.Content = JsonContent.Create(body, options: JsonOptions);
                retry.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);
                return await http.SendAsync(retry, ct);
            }
            throw new HttpRequestException($"API trả lỗi {code}.");
        }
        return response;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch { return null; }
    }
}
