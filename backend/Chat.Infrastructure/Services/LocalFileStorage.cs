using Chat.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Services;

public sealed class FileStorageOptions
{
    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chat_zalo", "private-files");
}

// This directory is never exposed through UseStaticFiles. Keys are server-generated,
// relative identifiers shared by Avatar and Attachment services, not client filenames.
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly string root = Path.GetFullPath(options.Value.RootPath);

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || key.Contains('\\')
            || key.Contains(':') || key.Split('/').Any(p => p is "" or "." or ".."
                || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
        {
            if (directory.LinkTarget is not null) throw new IOException("Linked storage directories are not supported.");
            if (directory.FullName == root) break;
        }
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Linked storage files are not supported.");
        return path;
    }

    public async Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
                await content.CopyToAsync(target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(Resolve(storageKey), FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Resolve(storageKey)); // Missing files make manual cleanup retryable.
        return Task.CompletedTask;
    }
}
