using System.Net.Http.Headers;
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

    // Đường dẫn tương đối của backend (ví dụ /api/users/{id}/avatar) -> địa chỉ đầy đủ để đặt vào <img src>, <a href>
    public Uri ToAbsoluteUri(string pathOrUrl) =>
        // Phải kiểm tra http/https: trên trình duyệt, chuỗi "/api/..." cũng bị .NET coi là đường dẫn tệp tuyệt đối (file:///api/...)
        Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute)
        && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute
            : new Uri(_http.BaseAddress!, pathOrUrl.TrimStart('/'));

    public async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, ToJson(body));
        return await ReadBodyAsync<T>(response);
    }

    public async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, ToJson(body));
    }

    // Gửi một tệp bằng multipart/form-data (hợp đồng Sprint 3: trường tên "file").
    // Vẫn gắn cookie và X-CSRF-TOKEN như mọi yêu cầu ghi khác.
    public async Task<T> SendFileAsync<T>(HttpMethod method, string url, Stream content, string fileName, string contentType)
    {
        using var response = await SendCoreAsync(method, url, ToMultipart(content, fileName, contentType));
        return await ReadBodyAsync<T>(response);
    }

    private static HttpContent? ToJson(object? body) =>
        body is null ? null : JsonContent.Create(body, body.GetType());

    private static MultipartFormDataContent ToMultipart(Stream content, string fileName, string contentType)
    {
        var file = new StreamContent(content);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            file.Headers.ContentType = mediaType;

        // Không tự đặt Content-Type cho cả khối: trình duyệt tự thêm boundary đúng chuẩn
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException((int)response.StatusCode, "EMPTY_RESPONSE", "Empty response body");
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, url);

        // Cho trình duyệt gửi/nhận cookie đăng nhập khi API khác origin
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        if (content is not null)
            request.Content = content;

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
