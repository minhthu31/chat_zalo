namespace Chat.Application.Interfaces;

public interface IPendingFileCleanupService
{
    Task<int> CleanupAsync(DateTimeOffset uploadedBefore, int limit = 100,
        CancellationToken cancellationToken = default);
}
