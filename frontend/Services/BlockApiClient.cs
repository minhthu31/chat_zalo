namespace Chat.Client.Services;

public sealed record BlockedUserDto(Guid UserId, string Username, string DisplayName, DateTimeOffset CreatedAt);
public interface IBlockApiClient
{
    Task<List<BlockedUserDto>> GetAsync();
    Task BlockAsync(Guid userId);
    Task UnblockAsync(Guid userId);
}
public sealed class BlockApiClient(ApiRequestSender api) : IBlockApiClient
{
    public Task<List<BlockedUserDto>> GetAsync() => api.SendAsync<List<BlockedUserDto>>(HttpMethod.Get, "api/blocks");
    public Task BlockAsync(Guid userId) => api.SendAsync(HttpMethod.Put, $"api/blocks/{userId}");
    public Task UnblockAsync(Guid userId) => api.SendAsync(HttpMethod.Delete, $"api/blocks/{userId}");
}
