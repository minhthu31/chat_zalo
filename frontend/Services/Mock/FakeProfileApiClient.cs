using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeProfileApiClient : IProfileApiClient
{
    private static readonly Guid Me = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");

    private ProfileDto _me = new()
    {
        Id = Me,
        Username = "user01",
        DisplayName = "Nguyen Van A",
        Bio = "",
        AvatarUrl = null
    };

    public async Task<ProfileDto> GetAsync(Guid userId)
    {
        await Task.Delay(200);

        if (userId == Me)
            return Copy(_me);

        var user = FakeUserApiClient.Directory.FirstOrDefault(u => u.Id == userId)
                   ?? throw new ApiException(404, "USER_NOT_FOUND", "User not found");

        return new ProfileDto { Id = user.Id, Username = user.Username, DisplayName = user.DisplayName };
    }

    public async Task<ProfileDto> UpdateMyAsync(SaveProfileRequest request)
    {
        await Task.Delay(300);

        var name = request.DisplayName.Trim();
        if (name.Length is < 1 or > ProfileLimits.DisplayNameMax)
            throw new ApiException(400, "INVALID_DISPLAY_NAME", "Display name must be between 1 and 50 characters.");

        if (request.Bio.Length > ProfileLimits.BioMax)
            throw new ApiException(400, "INVALID_BIO", "Bio must not exceed 200 characters.");

        _me.DisplayName = name;
        _me.Bio = request.Bio;
        return Copy(_me);
    }

    public async Task<ProfileDto> UploadAvatarAsync(Stream content, string fileName, string contentType)
    {
        await Task.Delay(400);

        if (!ProfileLimits.AvatarContentTypes.Contains(contentType))
            throw new ApiException(415, "UNSUPPORTED_FILE_TYPE", "Unsupported file type");

        _me.AvatarUrl = $"/api/users/{Me}/avatar";
        return Copy(_me);
    }

    public async Task DeleteAvatarAsync()
    {
        await Task.Delay(200);
        _me.AvatarUrl = null;
    }

    private static ProfileDto Copy(ProfileDto p) =>
        new() { Id = p.Id, Username = p.Username, DisplayName = p.DisplayName, Bio = p.Bio, AvatarUrl = p.AvatarUrl };
}
