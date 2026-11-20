using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Chat.Application.Services;

namespace Chat.Infrastructure.Services;

public static class AttachmentContentValidator
{
    public const int MaxBytes = 10 * 1024 * 1024;

    // Content-Type is derived from verified bytes/container structure, never from
    // the multipart Content-Type or a filename alone. This is not a malware scanner.
    public static string Validate(string extension, byte[] bytes)
    {
        var b = bytes.AsSpan();
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => IsJpeg(b),
            ".png" => IsPng(b),
            ".webp" => IsWebP(b),
            ".pdf" => b.Length >= 12 && b.StartsWith("%PDF-"u8)
                && b[Math.Max(0, b.Length - 1024)..].IndexOf("%%EOF"u8) >= 0,
            ".docx" or ".xlsx" => IsOffice(extension, bytes),
            ".txt" => IsText(bytes),
            _ => false
        };
        if (!valid) throw new AttachmentServiceException(415, "UNSUPPORTED_FILE_TYPE", "Unsupported or invalid file content.");
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
            ".pdf" => "application/pdf", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _ => "text/plain"
        };
    }

    private static bool IsJpeg(ReadOnlySpan<byte> b)
    {
        if (b.Length < 16 || !b.StartsWith(new byte[] { 255, 216, 255 }) || !b.EndsWith(new byte[] { 255, 217 })) return false;
        var frame = false;
        for (var i = 2; i + 4 <= b.Length;)
        {
            if (b[i++] != 255) return false;
            while (i < b.Length && b[i] == 255) i++;
            if (i >= b.Length) return false;
            var marker = b[i++];
            if (marker is 0 or 216 or 217) return false;
            if (marker is 1 or >= 208 and <= 215) continue;
            if (i + 2 > b.Length) return false;
            var size = BinaryPrimitives.ReadUInt16BigEndian(b[i..]);
            if (size < 2 || size > b.Length - i) return false;
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
                frame = size >= 8 && BinaryPrimitives.ReadUInt16BigEndian(b[(i + 3)..]) > 0
                    && BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]) > 0;
            if (marker == 218) return frame && size >= 6 && i + size < b.Length - 2;
            i += size;
        }
        return false;
    }

    private static bool IsPng(ReadOnlySpan<byte> b)
    {
        if (!b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var image = false; var header = false;
        for (var i = 8; i + 12 <= b.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(b[i..]);
            if (size > b.Length - i - 12) return false;
            var n = (int)size;
            var type = b.Slice(i + 4, 4);
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || n != 13
                    || BinaryPrimitives.ReadUInt32BigEndian(b[(i + 8)..]) == 0
                    || BinaryPrimitives.ReadUInt32BigEndian(b[(i + 12)..]) == 0) return false;
                header = true;
            }
            if (type.SequenceEqual("IDAT"u8) && n > 0) image = true;
            if (type.SequenceEqual("IEND"u8)) return header && image && n == 0 && i + 12 == b.Length;
            i += n + 12;
        }
        return false;
    }

    private static bool IsWebP(ReadOnlySpan<byte> b)
    {
        if (b.Length < 20 || !b.StartsWith("RIFF"u8) || !b.Slice(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(b[4..]) != b.Length - 8) return false;
        return HasWebPImage(b[12..]);
    }

    private static bool HasWebPImage(ReadOnlySpan<byte> chunks, bool allowFrames = true)
    {
        var image = false;
        for (var i = 0; i + 8 <= chunks.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunks[(i + 4)..]);
            if (size > chunks.Length - i - 8) return false;
            var n = (int)size; var data = chunks.Slice(i + 8, n); var type = chunks.Slice(i, 4);
            if (type.SequenceEqual("VP8 "u8)) image |= n >= 10 && (data[0] & 1) == 0 && data.Slice(3, 3).SequenceEqual(new byte[] { 157, 1, 42 });
            if (type.SequenceEqual("VP8L"u8)) image |= n >= 5 && data[0] == 47;
            if (allowFrames && type.SequenceEqual("ANMF"u8)) image |= n >= 24 && HasWebPImage(data[16..], false);
            i += 8 + n + (n % 2);
            if (i > chunks.Length) return false;
        }
        return image;
    }

    private static bool IsText(byte[] b)
    {
        if (b.AsSpan().StartsWith("%PDF-"u8) || b.AsSpan().StartsWith("PK"u8)) return false;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(b);
            return text.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t');
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool IsOffice(string extension, byte[] b)
    {
        try
        {
            using var stream = new MemoryStream(b, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var path = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
            var rootName = extension == ".docx" ? "document" : "workbook";
            var rootNs = extension == ".docx" ? "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                : "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var contentType = extension == ".docx" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
            if (zip.Entries.Count > 10000 || zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))) return false;
            var types = zip.GetEntry("[Content_Types].xml"); var entry = zip.GetEntry(path);
            if (types is null || entry is null || types.Length > 1024 * 1024) return false;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            var declared = false;
            using (var xml = XmlReader.Create(types.Open(), settings))
                while (xml.Read())
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Override"
                        && xml.GetAttribute("PartName") == "/" + path && xml.GetAttribute("ContentType") == contentType) declared = true;
            using var document = XmlReader.Create(entry.Open(), settings);
            document.MoveToContent();
            return declared && document.LocalName == rootName && document.NamespaceURI == rootNs;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or ArgumentException) { return false; }
    }
}
