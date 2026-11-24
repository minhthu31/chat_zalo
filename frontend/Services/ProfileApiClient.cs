using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IProfileApiClient
public class ProfileApiClient : IProfileApiClient
{
    private readonly ApiRequestSender _api;

    public ProfileApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<ProfileDto> GetAsync(Guid userId) =>
        _api.SendAsync<ProfileDto>(HttpMethod.Get, $"api/users/{userId}/profile");

    public Task<ProfileDto> UpdateMyAsync(SaveProfileRequest request) =>
        _api.SendAsync<ProfileDto>(HttpMethod.Put, "api/users/me/profile", request);

    public Task<ProfileDto> UploadAvatarAsync(Stream content, string fileName, string contentType) =>
        _api.SendFileAsync<ProfileDto>(HttpMethod.Put, "api/users/me/avatar", content, fileName, contentType);

    public Task DeleteAvatarAsync() =>
        _api.SendAsync(HttpMethod.Delete, "api/users/me/avatar");
}
