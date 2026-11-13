namespace Chat.Client.Models;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

// Kết quả của GET /api/users/search/paged (hợp đồng Sprint 2, mục 9)
public class PagedUsersResponse
{
    public List<UserDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class UpdateProfileRequest
{
    public string DisplayName { get; set; } = "";
}