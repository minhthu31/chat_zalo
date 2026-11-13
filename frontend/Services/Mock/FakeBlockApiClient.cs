namespace Chat.Client.Services.Mock;

public class FakeBlockApiClient : IBlockApiClient
{
    private readonly HashSet<Guid> _blocked = new();

    public async Task BlockAsync(Guid userId)
    {
        await Task.Delay(300);

        if (userId == Guid.Empty)
            throw new ApiException(404, "NOT_FOUND", "Target user does not exist");

        _blocked.Add(userId); // bấm lặp lại không lỗi, giống backend thật
    }

    public async Task UnblockAsync(Guid userId)
    {
        await Task.Delay(300);

        if (userId == Guid.Empty)
            throw new ApiException(404, "NOT_FOUND", "Target user does not exist");

        _blocked.Remove(userId);
    }
}
