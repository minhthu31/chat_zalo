namespace Chat.Application.Services;

public sealed class ReceiptServiceException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ReceiptServiceException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
