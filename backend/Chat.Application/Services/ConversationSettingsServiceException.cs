namespace Chat.Application.Services;

public sealed class ConversationSettingsServiceException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ConversationSettingsServiceException(
        int statusCode,
        string code,
        string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

