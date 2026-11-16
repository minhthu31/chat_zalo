namespace Chat.Application.DTOs.Users;

public sealed record BlockedUserDto(Guid UserId, string Username, string DisplayName, DateTimeOffset CreatedAt);
