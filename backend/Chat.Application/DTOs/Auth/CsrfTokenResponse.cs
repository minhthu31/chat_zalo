namespace Chat.Application.DTOs.Auth;

/// <summary>Response for GET /api/auth/csrf, matching API_CONTRACT v1.2.</summary>
public sealed class CsrfTokenResponse
{
    public string Token { get; set; } = string.Empty;
}
