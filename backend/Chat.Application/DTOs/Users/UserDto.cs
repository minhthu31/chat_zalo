namespace Chat.Application.DTOs.Users;

public sealed class UserDto
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}