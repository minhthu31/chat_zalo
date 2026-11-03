namespace Chat.Application.Services;

/// <summary>Business error translated by the API controller to { code, message }.</summary>
public sealed class FriendshipException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public FriendshipException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public static FriendshipException Validation(string message) =>
        new("VALIDATION_ERROR", message, 400);
}
