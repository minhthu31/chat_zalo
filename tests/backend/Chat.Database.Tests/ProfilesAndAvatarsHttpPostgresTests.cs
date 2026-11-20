using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chat.Api.Controllers;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public class ProfilesAndAvatarsHttpPostgresTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jG3sAAAAASUVORK5CYII=");
    private static readonly byte[] ValidJpegBytes = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8qqKKKAP/2Q==");
    private static readonly byte[] ValidWebpBytes = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    private sealed class Factory : WebApplicationFactory<ProfilesController>
    {
        private readonly string storagePath = Path.Combine(Path.GetTempPath(), "chat24-avatar-tests-" + Guid.NewGuid().ToString("N"));
        public string StoragePath => storagePath;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(storagePath))
            {
                try { Directory.Delete(storagePath, recursive: true); } catch { }
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var cs = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated test database.");
            var database = new NpgsqlConnectionStringBuilder(cs).Database ?? "";
            if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
                && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A dedicated test/review database is required.");

            builder.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:DefaultConnection", cs)
                .UseSetting("ConnectionStrings:ChatDb", cs)
                .UseSetting("FileStorage:RootPath", storagePath);
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static async Task Error(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        var json = await Json(r);
        Assert.Equal(code, json.GetProperty("code").GetString());
    }

    private sealed class Session : IDisposable
    {
        private readonly HttpClient client;
        public Uri BaseAddress => client.BaseAddress!;

        public Session(Factory factory)
        {
            client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            client.BaseAddress = new UriBuilder(addresses.Addresses.Single()) { Host = "localhost" }.Uri;
        }

        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool csrf = true, bool invalidCsrf = false)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                if (body is string rawJson)
                {
                    request.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");
                }
                else
                {
                    request.Content = JsonContent.Create(body);
                }
            }
            if (method != HttpMethod.Get && csrf)
            {
                var token = (await Json(await client.GetAsync("/api/auth/csrf"))).GetProperty("token").GetString();
                request.Headers.Add("X-CSRF-TOKEN", invalidCsrf ? "invalid" : token);
            }
            return await client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> UploadAvatar(byte[] bytes, string filename = "avatar.png", string type = "image/png", bool csrf = true, string field = "file")
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me/avatar");
            request.Headers.ExpectContinue = true;
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(type);
            form.Add(part, field, filename);
            request.Content = form;
            if (csrf)
            {
                var token = (await Json(await client.GetAsync("/api/auth/csrf"))).GetProperty("token").GetString();
                request.Headers.Add("X-CSRF-TOKEN", token);
            }
            return await client.SendAsync(request);
        }

        public async Task<Guid> Register()
        {
            var name = "prof" + Guid.NewGuid().ToString("N");
            var r = await Send(HttpMethod.Post, "/api/auth/register", new { username = name, email = name + "@example.com", password = "Review123!" });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var id = (await Json(r)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/auth/login", new { usernameOrEmail = name, password = "Review123!" })).StatusCode);
            return id;
        }

        public void Dispose() => client.Dispose();
    }

    [Fact]
    public async Task GetProfile_Authenticated_SelfAndOthers_EvenWhenBlocked_PreservesLongDisplayName_RejectsAnonymousAndMissing()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        using var b = new Session(factory);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonymous.BaseAddress = a.BaseAddress;

        var aid = await a.Register();
        var bid = await b.Register();

        // 1. Chưa đăng nhập -> 401
        var anonRes = await anonymous.GetAsync($"/api/users/{aid}/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        // 2. User không tồn tại -> 404 USER_NOT_FOUND
        var missingRes = await a.Send(HttpMethod.Get, $"/api/users/{Guid.NewGuid()}/profile");
        await Error(missingRes, HttpStatusCode.NotFound, "USER_NOT_FOUND");

        // 3. User A xem hồ sơ chính mình
        var selfRes = await a.Send(HttpMethod.Get, $"/api/users/{aid}/profile");
        Assert.Equal(HttpStatusCode.OK, selfRes.StatusCode);
        var selfJson = await Json(selfRes);
        Assert.Equal(aid, selfJson.GetProperty("id").GetGuid());
        Assert.False(selfJson.TryGetProperty("email", out _));
        Assert.Null(selfJson.GetProperty("avatarUrl").GetString());

        // 4. User A xem hồ sơ User B
        var otherRes = await a.Send(HttpMethod.Get, $"/api/users/{bid}/profile");
        Assert.Equal(HttpStatusCode.OK, otherRes.StatusCode);
        var otherJson = await Json(otherRes);
        Assert.Equal(bid, otherJson.GetProperty("id").GetGuid());
        Assert.False(otherJson.TryGetProperty("email", out _));

        // 5. Hai người chặn nhau: A chặn B
        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Put, $"/api/blocks/{bid}")).StatusCode);

        // Kể cả khi chặn nhau, người đăng nhập vẫn xem được hồ sơ công khai
        var bViewsA = await b.Send(HttpMethod.Get, $"/api/users/{aid}/profile");
        Assert.Equal(HttpStatusCode.OK, bViewsA.StatusCode);
        var aViewsB = await a.Send(HttpMethod.Get, $"/api/users/{bid}/profile");
        Assert.Equal(HttpStatusCode.OK, aViewsB.StatusCode);

        // 6. User có DisplayName cũ dài > 50 ký tự (ví dụ 80 ký tự) vẫn đọc nguyên vẹn, không bị cắt
        var longDisplayName = new string('x', 80);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var userB = await db.Users.SingleAsync(u => u.Id == bid);
            userB.DisplayName = longDisplayName;
            await db.SaveChangesAsync();
        }

        var longNameRes = await a.Send(HttpMethod.Get, $"/api/users/{bid}/profile");
        Assert.Equal(HttpStatusCode.OK, longNameRes.StatusCode);
        var longNameJson = await Json(longNameRes);
        Assert.Equal(longDisplayName, longNameJson.GetProperty("displayName").GetString());

        // Kiểm tra trong DB không bị sửa đổi
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var userB = await db.Users.SingleAsync(u => u.Id == bid);
            Assert.Equal(longDisplayName, userB.DisplayName);
        }
    }

    [Fact]
    public async Task UpdateProfile_ValidatesDisplayNameAndBio_RequiresCsrf_SavesToDatabase()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        var aid = await a.Register();

        // 1. Thiếu hoặc sai CSRF Token -> 400 INVALID_CSRF_TOKEN
        var noCsrf = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = "Valid Name", bio = "Bio" }, csrf: false);
        await Error(noCsrf, HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");

        var badCsrf = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = "Valid Name", bio = "Bio" }, invalidCsrf: true);
        await Error(badCsrf, HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");

        // 2. DisplayName rỗng / toàn khoảng trắng -> 400 INVALID_DISPLAY_NAME
        var emptyName = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = "", bio = "Bio" });
        await Error(emptyName, HttpStatusCode.BadRequest, "INVALID_DISPLAY_NAME");

        var whitespaceName = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = "   ", bio = "Bio" });
        await Error(whitespaceName, HttpStatusCode.BadRequest, "INVALID_DISPLAY_NAME");

        // 3. DisplayName > 50 ký tự sau trim -> 400 INVALID_DISPLAY_NAME
        var longName = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = new string('d', 51), bio = "Bio" });
        await Error(longName, HttpStatusCode.BadRequest, "INVALID_DISPLAY_NAME");

        // 4. Bio > 200 ký tự -> 400 INVALID_BIO
        var longBio = await a.Send(HttpMethod.Put, "/api/users/me/profile", new { displayName = "Valid Name", bio = new string('b', 201) });
        await Error(longBio, HttpStatusCode.BadRequest, "INVALID_BIO");

        // 5. Cập nhật hợp lệ (có trim khoảng trắng ở DisplayName, Bio tối đa 200 ký tự)
        var validRes = await a.Send(HttpMethod.Put, "/api/users/me/profile", new
        {
            displayName = "  Nguyen Van A  ",
            bio = "Xin chao toi la Nguyen Van A."
        });
        Assert.Equal(HttpStatusCode.OK, validRes.StatusCode);
        var dto = await Json(validRes);
        Assert.Equal("Nguyen Van A", dto.GetProperty("displayName").GetString());
        Assert.Equal("Xin chao toi la Nguyen Van A.", dto.GetProperty("bio").GetString());

        // Kiểm tra lưu DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.Equal("Nguyen Van A", user.DisplayName);
            Assert.Equal("Xin chao toi la Nguyen Van A.", user.Bio);
        }
    }

    [Fact]
    public async Task AvatarLifecycle_Upload_Download_Replace_Delete_ValidatesFormatSizeAndCsrf()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        using var b = new Session(factory);
        var aid = await a.Register();
        var bid = await b.Register();

        // 1. Khi chưa có avatar -> GET /api/users/{aid}/avatar trả 404 ATTACHMENT_NOT_FOUND
        var noAvatarRes = await b.Send(HttpMethod.Get, $"/api/users/{aid}/avatar");
        await Error(noAvatarRes, HttpStatusCode.NotFound, "ATTACHMENT_NOT_FOUND");

        // User không tồn tại -> 404 USER_NOT_FOUND
        var noUserAvatar = await b.Send(HttpMethod.Get, $"/api/users/{Guid.NewGuid()}/avatar");
        await Error(noUserAvatar, HttpStatusCode.NotFound, "USER_NOT_FOUND");

        // 2. Upload avatar thiếu CSRF -> 400 INVALID_CSRF_TOKEN
        var missingCsrf = await a.UploadAvatar(ValidPngBytes, csrf: false);
        await Error(missingCsrf, HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");

        // 3. Upload file vượt quá 2 MiB -> 413 FILE_TOO_LARGE
        var oversized = new byte[2 * 1024 * 1024 + 1];
        var largeRes = await a.UploadAvatar(oversized, filename: "huge.png");
        await Error(largeRes, HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");

        // 4. Upload file sai loại (TXT hoặc file giả đuôi .png) -> 415 UNSUPPORTED_FILE_TYPE
        var fakePng = Encoding.UTF8.GetBytes("Not a real png file header");
        var fakeRes = await a.UploadAvatar(fakePng, filename: "fake.png");
        await Error(fakeRes, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE");

        // 5. Upload hợp lệ ảnh PNG
        var uploadPngRes = await a.UploadAvatar(ValidPngBytes, filename: "test.png", type: "image/png");
        Assert.Equal(HttpStatusCode.OK, uploadPngRes.StatusCode);
        var profileDto = await Json(uploadPngRes);
        Assert.Equal($"/api/users/{aid}/avatar", profileDto.GetProperty("avatarUrl").GetString());

        Guid firstAvatarFileId;
        string firstStorageKey;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);
            firstAvatarFileId = user.AvatarFileId.Value;

            var storedFile = await db.StoredFiles.SingleAsync(f => f.Id == firstAvatarFileId);
            Assert.Equal("Avatar", storedFile.UsageType);
            Assert.Null(storedFile.ConversationId);
            Assert.Equal("Active", storedFile.Status);
            Assert.Equal("image/png", storedFile.ContentType);
            Assert.Equal(ValidPngBytes.Length, storedFile.SizeBytes);
            firstStorageKey = storedFile.StorageKey;
        }

        // Kiểm tra file vật lý đã ghi vào đĩa
        var firstPhysicalPath = Path.Combine(factory.StoragePath, firstStorageKey);
        Assert.True(File.Exists(firstPhysicalPath));

        // 6. User B tải avatar của User A (kể cả khi hai người chặn nhau)
        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Put, $"/api/blocks/{bid}")).StatusCode);

        var downloadRes = await b.Send(HttpMethod.Get, $"/api/users/{aid}/avatar");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        Assert.Equal("image/png", downloadRes.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", downloadRes.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("nosniff", downloadRes.Headers.GetValues("X-Content-Type-Options").Single());
        var downloadedBytes = await downloadRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(ValidPngBytes, downloadedBytes);

        // 7. Đổi sang avatar mới: Upload ảnh JPEG
        var uploadJpegRes = await a.UploadAvatar(ValidJpegBytes, filename: "new_avatar.jpg", type: "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, uploadJpegRes.StatusCode);

        Guid secondAvatarFileId;
        string secondStorageKey;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);
            Assert.NotEqual(firstAvatarFileId, user.AvatarFileId.Value);
            secondAvatarFileId = user.AvatarFileId.Value;

            // StoredFile cũ chuyển sang Inactive
            var oldFile = await db.StoredFiles.SingleAsync(f => f.Id == firstAvatarFileId);
            Assert.Equal("Inactive", oldFile.Status);

            // StoredFile mới Active
            var newFile = await db.StoredFiles.SingleAsync(f => f.Id == secondAvatarFileId);
            Assert.Equal("Active", newFile.Status);
            Assert.Equal("image/jpeg", newFile.ContentType);
            secondStorageKey = newFile.StorageKey;
        }

        // File vật lý cũ bị xóa khỏi storage
        Assert.False(File.Exists(firstPhysicalPath));
        // File mới tồn tại
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, secondStorageKey)));

        // 8. Xóa avatar: DELETE /api/users/me/avatar
        var deleteRes = await a.Send(HttpMethod.Delete, "/api/users/me/avatar");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.Null(user.AvatarFileId);

            var secondFile = await db.StoredFiles.SingleAsync(f => f.Id == secondAvatarFileId);
            Assert.Equal("Inactive", secondFile.Status);
        }

        // File vật lý bị xóa
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, secondStorageKey)));

        // Sau khi xóa: GET avatar trả 404 ATTACHMENT_NOT_FOUND
        var deletedGet = await b.Send(HttpMethod.Get, $"/api/users/{aid}/avatar");
        await Error(deletedGet, HttpStatusCode.NotFound, "ATTACHMENT_NOT_FOUND");

        // Profile trả avatarUrl = null
        var profileAfterDelete = await a.Send(HttpMethod.Get, $"/api/users/{aid}/profile");
        Assert.Equal(HttpStatusCode.OK, profileAfterDelete.StatusCode);
        Assert.Null((await Json(profileAfterDelete)).GetProperty("avatarUrl").GetString());

        // 9. Gọi DELETE lần nữa khi chưa có avatar -> vẫn trả 204 (idempotent)
        var deleteAgain = await a.Send(HttpMethod.Delete, "/api/users/me/avatar");
        Assert.Equal(HttpStatusCode.NoContent, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_MissingOrNullBio_Returns400_AndPreservesExistingData()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        var aid = await a.Register();

        // Cập nhật sẵn user có DisplayName và Bio không rỗng
        var initial = await a.Send(HttpMethod.Put, "/api/users/me/profile", new
        {
            displayName = "Ten ban dau",
            bio = "Bio cu can giu nguyen"
        });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        // Gửi PUT /api/users/me/profile với raw JSON {"displayName":"Ten moi"} (thiếu hẳn key bio)
        // -> assert HTTP 400 (INVALID_BIO)
        var missingBio = await a.Send(HttpMethod.Put, "/api/users/me/profile", "{\"displayName\":\"Ten moi\"}");
        await Error(missingBio, HttpStatusCode.BadRequest, "INVALID_BIO");

        // Kiểm tra lại DB và GET /api/users/{id}/profile xác nhận cả DisplayName lẫn Bio cũ vẫn giữ nguyên 100%
        var profileAfterMissing = await a.Send(HttpMethod.Get, $"/api/users/{aid}/profile");
        Assert.Equal(HttpStatusCode.OK, profileAfterMissing.StatusCode);
        var jsonAfterMissing = await Json(profileAfterMissing);
        Assert.Equal("Ten ban dau", jsonAfterMissing.GetProperty("displayName").GetString());
        Assert.Equal("Bio cu can giu nguyen", jsonAfterMissing.GetProperty("bio").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.Equal("Ten ban dau", user.DisplayName);
            Assert.Equal("Bio cu can giu nguyen", user.Bio);
        }

        // Gửi PUT /api/users/me/profile với raw JSON {"displayName":"Ten moi","bio":null}
        // -> assert HTTP 400 (INVALID_BIO), dữ liệu DB giữ nguyên
        var nullBio = await a.Send(HttpMethod.Put, "/api/users/me/profile", "{\"displayName\":\"Ten moi\",\"bio\":null}");
        await Error(nullBio, HttpStatusCode.BadRequest, "INVALID_BIO");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.Equal("Ten ban dau", user.DisplayName);
            Assert.Equal("Bio cu can giu nguyen", user.Bio);
        }

        // Gửi PUT /api/users/me/profile với {"displayName":"Ten moi","bio":""}
        // -> assert HTTP 200, Bio được cập nhật thành ""
        var emptyBio = await a.Send(HttpMethod.Put, "/api/users/me/profile", "{\"displayName\":\"Ten moi\",\"bio\":\"\"}");
        Assert.Equal(HttpStatusCode.OK, emptyBio.StatusCode);
        var jsonAfterEmpty = await Json(emptyBio);
        Assert.Equal("Ten moi", jsonAfterEmpty.GetProperty("displayName").GetString());
        Assert.Equal("", jsonAfterEmpty.GetProperty("bio").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.Equal("Ten moi", user.DisplayName);
            Assert.Equal("", user.Bio);
        }
    }

    [Fact]
    public async Task AvatarService_StaleTrackedUser_RemoveAsync_ReloadsAndRemovesActiveAvatar()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        var aid = await a.Register();

        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ChatDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ChatDbContext>();
        var service1 = scope1.ServiceProvider.GetRequiredService<IAvatarService>();
        var service2 = scope2.ServiceProvider.GetRequiredService<IAvatarService>();

        // 1. db1 load sẵn ApplicationUser (tracked, AvatarFileId == null)
        var trackedUserInDb1 = await db1.Users.SingleAsync(u => u.Id == aid);
        Assert.Null(trackedUserInDb1.AvatarFileId);

        // 2. db2 gọi AvatarService.SetAsync tạo Avatar A (Active)
        using var streamA = new MemoryStream(ValidPngBytes);
        var setResA = await service2.SetAsync(aid, "avatarA.png", "image/png", ValidPngBytes.Length, streamA);
        Assert.NotNull(setResA.AvatarUrl);

        Guid avatarAId;
        string storageKeyA;
        using (var verifyScope = factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await verifyDb.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);
            avatarAId = user.AvatarFileId.Value;
            var fileA = await verifyDb.StoredFiles.SingleAsync(f => f.Id == avatarAId);
            Assert.Equal("Active", fileA.Status);
            storageKeyA = fileA.StorageKey;
        }
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, storageKeyA)));

        // 3. db1 vẫn đang giữ trackedUserInDb1 với AvatarFileId == null.
        // Gọi service1.RemoveAsync trên db1 -> nhờ ReloadAsync, nó phải nhận ra Avatar A và chuyển sang Inactive!
        await service1.RemoveAsync(aid);

        using (var verifyScope = factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await verifyDb.Users.SingleAsync(u => u.Id == aid);
            Assert.Null(user.AvatarFileId);

            var fileA = await verifyDb.StoredFiles.SingleAsync(f => f.Id == avatarAId);
            Assert.Equal("Inactive", fileA.Status);
        }
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, storageKeyA)));
    }

    [Fact]
    public async Task AvatarService_StaleTrackedUser_SetAsync_ReloadsAndInactivatesOldAvatar()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        var aid = await a.Register();

        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ChatDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ChatDbContext>();
        var service1 = scope1.ServiceProvider.GetRequiredService<IAvatarService>();
        var service2 = scope2.ServiceProvider.GetRequiredService<IAvatarService>();

        // 1. db1 load sẵn ApplicationUser (tracked, AvatarFileId == null)
        var trackedUserInDb1 = await db1.Users.SingleAsync(u => u.Id == aid);
        Assert.Null(trackedUserInDb1.AvatarFileId);

        // 2. db2 gọi AvatarService.SetAsync tạo Avatar A (Active)
        using var streamA = new MemoryStream(ValidPngBytes);
        await service2.SetAsync(aid, "avatarA.png", "image/png", ValidPngBytes.Length, streamA);

        Guid avatarAId;
        string storageKeyA;
        using (var verifyScope = factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await verifyDb.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);
            avatarAId = user.AvatarFileId.Value;
            var fileA = await verifyDb.StoredFiles.SingleAsync(f => f.Id == avatarAId);
            Assert.Equal("Active", fileA.Status);
            storageKeyA = fileA.StorageKey;
        }
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, storageKeyA)));

        // 3. db1 gọi SetAsync với Avatar B. Nhờ ReloadAsync, db1 nhận ra Avatar A cũ và chuyển sang Inactive!
        using var streamB = new MemoryStream(ValidJpegBytes);
        await service1.SetAsync(aid, "avatarB.jpg", "image/jpeg", ValidJpegBytes.Length, streamB);

        using (var verifyScope = factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await verifyDb.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);
            Assert.NotEqual(avatarAId, user.AvatarFileId.Value);

            // Chỉ có DUY NHẤT 1 StoredFile Active
            var activeFiles = await verifyDb.StoredFiles.Where(f => f.UploadedByUserId == aid && f.UsageType == "Avatar" && f.Status == "Active").ToListAsync();
            var singleActive = Assert.Single(activeFiles);
            Assert.Equal(user.AvatarFileId.Value, singleActive.Id);

            var fileA = await verifyDb.StoredFiles.SingleAsync(f => f.Id == avatarAId);
            Assert.Equal("Inactive", fileA.Status);
        }
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, storageKeyA)));
    }

    [Fact]
    public async Task AvatarUpload_ConcurrentPuts_ResolvesCleanly_WithExactlyOneActiveStoredFile()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a1 = new Session(factory);
        var aid = await a1.Register();
        using var a2 = new Session(factory);
        // a2 login as the same user
        var name = (await Json(await a1.Send(HttpMethod.Get, $"/api/users/{aid}/profile"))).GetProperty("username").GetString()!;
        var loginRes = await a2.Send(HttpMethod.Post, "/api/auth/login", new { usernameOrEmail = name, password = "Review123!" });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // Gửi đồng thời 2 request PUT /api/users/me/avatar bằng Task.WhenAll cho cùng một user
        var t1 = a1.UploadAvatar(ValidPngBytes, filename: "avatar1.png", type: "image/png");
        var t2 = a2.UploadAvatar(ValidJpegBytes, filename: "avatar2.jpg", type: "image/jpeg");

        var responses = await Task.WhenAll(t1, t2);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == aid);
            Assert.NotNull(user.AvatarFileId);

            var avatarFiles = await db.StoredFiles.Where(f => f.UploadedByUserId == aid && f.UsageType == "Avatar").ToListAsync();
            Assert.Equal(2, avatarFiles.Count);

            var activeFiles = avatarFiles.Where(f => f.Status == "Active").ToList();
            var singleActive = Assert.Single(activeFiles);
            Assert.Equal(user.AvatarFileId.Value, singleActive.Id);

            var inactiveFiles = avatarFiles.Where(f => f.Status == "Inactive").ToList();
            Assert.Single(inactiveFiles);
        }
    }
}
