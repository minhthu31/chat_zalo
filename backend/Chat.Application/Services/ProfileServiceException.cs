namespace Chat.Application.Services;

public sealed class ProfileServiceException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
