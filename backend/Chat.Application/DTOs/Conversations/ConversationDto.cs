using System.Text.Json.Serialization;

namespace Chat.Application.DTOs.Conversations;

public class ConversationDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Direct";
    public long LastMessageSeq { get; set; }

    /// <summary>
    /// Internal flag used by the Controller to decide HTTP 201 vs 200.
    /// Excluded from the JSON response body.
    /// </summary>
    [JsonIgnore]
    public bool IsNewlyCreated { get; set; }
}
