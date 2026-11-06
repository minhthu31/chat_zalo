using Chat.Client.Models;

namespace Chat.Client.Services;

public interface IUserApiClient
{
    Task<UserDto> GetProfileAsync();                                  // GET /api/users/me
    Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request);   // PATCH /api/users/me
    Task<List<UserDto>> SearchAsync(string username);                 // GET /api/users/search?username=
}
