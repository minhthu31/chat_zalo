namespace Chat.Application.Services;

public class GroupServiceException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public GroupServiceException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
