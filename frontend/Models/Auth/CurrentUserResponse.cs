namespace Chat.Client.Models.Auth;

public sealed record CurrentUserResponse(Guid Id, string Username, string DisplayName);
