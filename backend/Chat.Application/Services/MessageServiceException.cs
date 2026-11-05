using System;

namespace Chat.Application.Services;

public sealed class MessageServiceException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public MessageServiceException(
        int statusCode,
        string code,
        string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}