namespace Chat.Client.Services;

// Bản thật của IBlockApiClient
public class BlockApiClient : IBlockApiClient
{
    private readonly ApiRequestSender _api;

    public BlockApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task BlockAsync(Guid userId) =>
        _api.SendAsync(HttpMethod.Put, $"api/blocks/{userId}");

    public Task UnblockAsync(Guid userId) =>
        _api.SendAsync(HttpMethod.Delete, $"api/blocks/{userId}");
}
