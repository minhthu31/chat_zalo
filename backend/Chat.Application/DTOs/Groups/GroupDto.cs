namespace Chat.Application.DTOs.Groups;

public class GroupDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Group";
    public string Name { get; set; } = string.Empty;
    public long LastMessageSeq { get; set; }
}
