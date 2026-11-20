namespace Chat.Application.DTOs.Friendships;

/// <summary>Public identity fields for an Accepted friend in search results.</summary>
public sealed class SearchFriendDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
