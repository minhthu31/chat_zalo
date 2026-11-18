namespace Chat.Application.Interfaces;

// Storage keys are private server-side identifiers, never public URLs.
public interface IFileStorage
{
    Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
