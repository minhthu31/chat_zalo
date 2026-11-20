namespace Chat.Application.Services;

public sealed class AttachmentServiceException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
