using System.Text.Json.Serialization;

namespace Chat.Application.DTOs.Conversations;

public class ConversationDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Direct";
    public long LastMessageSeq { get; set; }

    // Chỉ dùng nội bộ để chọn HTTP 201/200, không đưa ra JSON.
    [JsonIgnore]
    public bool IsNewlyCreated { get; set; }
}