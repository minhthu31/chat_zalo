using System.IO.Compression;
using System.Text;
using Chat.Application.Services;
using Chat.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Application.Tests;

public sealed class FileStorageAndValidationTests
{
    [Theory]
    [InlineData("../outside")]
    [InlineData("/outside")]
    [InlineData("avatars/../../outside")]
    [InlineData("C:\\outside")]
    [InlineData("avatars\\outside")]
    public async Task StorageRejectsTraversalAndAbsoluteKeys(string key)
    {
        var root = Path.Combine(Path.GetTempPath(), "chat24-unit-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.WriteAsync(key, new MemoryStream([1, 2])));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task StorageRoundTrip_DoesNotOverwrite_AndSupportsAvatarKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "chat24-unit-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));
        try
        {
            await storage.WriteAsync("avatars/user/avatar.png", new MemoryStream([1, 2, 3]));
            await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync("avatars/user/avatar.png", new MemoryStream([4])));
            await using (var stream = await storage.OpenReadAsync("avatars/user/avatar.png"))
            {
                using var output = new MemoryStream(); await stream.CopyToAsync(output);
                Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
            }
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            await storage.DeleteAsync("avatars/user/avatar.png");
            await storage.DeleteAsync("avatars/user/avatar.png");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".exe")]
    public void FakeExtensionIsNotEnough(string extension)
    {
        var ex = Assert.Throws<AttachmentServiceException>(() => AttachmentContentValidator.Validate(extension, "not an image or document"u8.ToArray()));
        Assert.Equal(415, ex.StatusCode);
    }

    [Theory]
    [InlineData(".docx", "word/document.xml", "document", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")]
    [InlineData(".xlsx", "xl/workbook.xml", "workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")]
    public void OfficeFormatsRequireActualPackageAndCorrectDocumentType(string extension, string path, string root, string ns, string mime)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
                writer.Write($"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{path}\" ContentType=\"{mime}\" /></Types>");
            using var doc = new StreamWriter(zip.CreateEntry(path).Open());
            doc.Write($"<{root} xmlns=\"{ns}\" />");
        }
        Assert.StartsWith("application/vnd.openxmlformats", AttachmentContentValidator.Validate(extension, stream.ToArray()));
        Assert.Throws<AttachmentServiceException>(() => AttachmentContentValidator.Validate(extension == ".docx" ? ".xlsx" : ".docx", stream.ToArray()));
    }

    [Fact]
    public void TextMustBeUtf8AndNotBinaryOrPdf()
    {
        Assert.Equal("text/plain", AttachmentContentValidator.Validate(".txt", Encoding.UTF8.GetBytes("Hello\nTiếng Việt")));
        Assert.Throws<AttachmentServiceException>(() => AttachmentContentValidator.Validate(".txt", [255, 0, 1]));
        Assert.Throws<AttachmentServiceException>(() => AttachmentContentValidator.Validate(".txt", "%PDF-1.4\n%%EOF"u8.ToArray()));
    }

    [Fact]
    public void RealPngJpegWebpAndPdfAreAccepted()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jG3sAAAAASUVORK5CYII=");
        Assert.Equal("image/png", AttachmentContentValidator.Validate(".png", png));
        var jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8qqKKKAP/2Q==");
        Assert.Equal("image/jpeg", AttachmentContentValidator.Validate(".jpg", jpeg));
        var webp = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");
        Assert.Equal("image/webp", AttachmentContentValidator.Validate(".webp", webp));
        Assert.Equal("application/pdf", AttachmentContentValidator.Validate(".pdf", "%PDF-1.4\n1 0 obj <<>> endobj\n%%EOF\n"u8.ToArray()));
        Assert.Throws<AttachmentServiceException>(() => AttachmentContentValidator.Validate(".png", png[..16]));
    }
}
