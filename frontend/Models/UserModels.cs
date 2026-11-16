namespace Chat.Client.Models;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class UpdateProfileRequest
{
    public string DisplayName { get; set; } = "";
}