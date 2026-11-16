using System.Net.Http.Json;
using Chat.Client.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Chat.Client.Services;

// Dùng chung cho các client thật: gửi cookie, gửi X-CSRF-TOKEN, đổi lỗi JSON thành ApiException
public class ApiRequestSender
{
    private readonly HttpClient _http;
    private string? _csrfToken;

    public ApiRequestSender(HttpClient http)
    {
        _http = http;
    }

    // Token gắn với danh tính hiện tại, nên bỏ token cũ sau khi đăng nhập/đăng xuất
    public void ResetCsrfToken() => _csrfToken = null;

    public async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException((int)response.StatusCode, "EMPTY_RESPONSE", "Empty response body");
    }

    public async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body)
    {
        using var request = new HttpRequestMessage(method, url);

        // Cho trình duyệt gửi/nhận cookie đăng nhập khi API khác origin
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        // Contract: mọi POST/PUT/PATCH/DELETE phải có X-CSRF-TOKEN
        if (method != HttpMethod.Get)
            request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(0, "NETWORK_ERROR", "Cannot reach the server");
        }

        if (response.IsSuccessStatusCode)
            return response;

        var error = await ReadErrorAsync(response);
        response.Dispose();
        throw error;
    }

    private async Task<string> GetCsrfTokenAsync()
    {
        _csrfToken ??= (await SendAsync<CsrfResponse>(HttpMethod.Get, "api/auth/csrf")).Token;
        return _csrfToken;
    }

    // Đọc { code, message } của server; nếu body không phải JSON thì dùng mã chung HTTP_xxx
    private static async Task<ApiException> ReadErrorAsync(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            if (error is not null && error.Code != "")
                return new ApiException(status, error.Code, error.Message);
        }
        catch (System.Text.Json.JsonException) { }

        return new ApiException(status, $"HTTP_{status}", "Request failed");
    }
}
