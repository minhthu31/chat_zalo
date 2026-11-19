namespace Chat.Application.DTOs.Files;

// The controller owns and disposes Content after streaming the response.
public sealed record FileContentResult(Stream Content, string ContentType, string FileName);
