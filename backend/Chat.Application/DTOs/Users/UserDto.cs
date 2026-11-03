namespace Chat.Application.DTOs.Users;

public sealed class UserDto
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
