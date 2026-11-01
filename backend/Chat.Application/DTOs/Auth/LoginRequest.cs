namespace Chat.Application.DTOs.Auth;

public class LoginRequest
{
    public string? UsernameOrEmail { get; set; }
    public string? Password { get; set; }
}
