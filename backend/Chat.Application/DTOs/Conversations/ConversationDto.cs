using System.Text.Json.Serialization;

namespace Chat.Application.DTOs.Conversations;

public class ConversationDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Direct";
    public Guid CreatedByUserId { get; set; }
    public long LastMessageSeq { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }

    // From ConversationMember (current user's row)
    public long LastReadSeq { get; set; }
    public long LastDeliveredSeq { get; set; }
    public long UnreadCount { get; set; }

    // For direct conversations: the other participant
    public ConversationParticipantDto? OtherParticipant { get; set; }

    // Preview of the last message
    public LastMessagePreviewDto? LastMessage { get; set; }

    /// <summary>True if either party has blocked the other.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Internal flag used by the Controller to decide HTTP 201 vs 200.
    /// Excluded from the JSON response body.
    /// </summary>
    [JsonIgnore]
    public bool IsNewlyCreated { get; set; }
}

public class ConversationParticipantDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class LastMessagePreviewDto
{
    public Guid Id { get; set; }
    public Guid SenderUserId { get; set; }
    public long Seq { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
