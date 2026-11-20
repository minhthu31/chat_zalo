using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chat.Database.Tests;

public partial class ConversationsHttpPostgresTests
{
    [Fact]
    public async Task NotificationSettings_DefaultPerAccountMultiSessionCsrfValidationAndPermissions_FollowContract()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a1 = new Session(factory);
        using var b = new Session(factory);
        using var c = new Session(factory);
        using var outsider = new Session(factory);

        var aid = await a1.Register();
        var bid = await b.Register();
        var cid = await c.Register();
        await outsider.Register();

        var userAInfo = await Json(await a1.Send(HttpMethod.Get, "/api/auth/me"));
        var usernameA = userAInfo.GetProperty("username").GetString()!;

        // Session 2 đăng nhập cùng tài khoản A (nhiều phiên / nhiều thiết bị)
        using var a2 = new Session(factory);
        Assert.Equal(
            HttpStatusCode.OK,
            (await a2.Send(HttpMethod.Post, "/api/auth/login", new { usernameOrEmail = usernameA, password = "Review123!" })).StatusCode);

        foreach (var pair in new[] { (bid, b), (cid, c) })
        {
            Assert.Equal(HttpStatusCode.Created, (await a1.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = pair.Item1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await pair.Item2.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept")).StatusCode);
        }

        // Tạo Direct Conversation (A - B) và Group Conversation (A, B, C)
        var directCreated = await a1.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid });
        Assert.Equal(HttpStatusCode.Created, directCreated.StatusCode);
        var directId = (await Json(directCreated)).GetProperty("id").GetGuid();

        var groupCreated = await a1.Send(HttpMethod.Post, "/api/conversations/groups", new { name = "Notification Group", memberIds = new[] { bid, cid } });
        Assert.Equal(HttpStatusCode.Created, groupCreated.StatusCode);
        var groupId = (await Json(groupCreated)).GetProperty("id").GetGuid();

        var directPath = $"/api/conversations/{directId}/notification-settings";
        var groupPath = $"/api/conversations/{groupId}/notification-settings";
        var missingPath = $"/api/conversations/{Guid.NewGuid()}/notification-settings";

        // Chưa đăng nhập -> 401 UNAUTHORIZED
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonymous.BaseAddress = a1.BaseAddress;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(directPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(directPath, new { isMuted = true })).StatusCode);

        // 1. Giá trị mặc định: isMuted = false, không tự tạo bản ghi
        var defaultA = await a1.Send(HttpMethod.Get, directPath);
        Assert.Equal(HttpStatusCode.OK, defaultA.StatusCode);
        Assert.False((await Json(defaultA)).GetProperty("isMuted").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            Assert.Equal(0, await db.ConversationNotificationSettings.CountAsync(
                s => s.ConversationId == directId && s.UserId == aid));
        }

        // 2. CSRF: thiếu hoặc sai token -> 400 INVALID_CSRF_TOKEN, không ghi dữ liệu
        await Error(await a1.Send(HttpMethod.Put, directPath, new { isMuted = true }, csrf: false), HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");
        await Error(await a1.Send(HttpMethod.Put, directPath, new { isMuted = true }, invalidCsrf: true), HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");
        Assert.False((await Json(await a1.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());

        // 3. Lưu riêng từng tài khoản: A PUT true, B GET vẫn false; B PUT false, A GET vẫn true
        var putATrue = await a1.Send(HttpMethod.Put, directPath, new { isMuted = true });
        Assert.Equal(HttpStatusCode.OK, putATrue.StatusCode);
        Assert.True((await Json(putATrue)).GetProperty("isMuted").GetBoolean());

        var getB = await b.Send(HttpMethod.Get, directPath);
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        Assert.False((await Json(getB)).GetProperty("isMuted").GetBoolean());

        var putBFalse = await b.Send(HttpMethod.Put, directPath, new { isMuted = false });
        Assert.Equal(HttpStatusCode.OK, putBFalse.StatusCode);
        Assert.False((await Json(putBFalse)).GetProperty("isMuted").GetBoolean());

        Assert.True((await Json(await a1.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());

        // 4. Nhiều phiên đăng nhập: Session 1 PUT true -> Session 2 GET true; Session 2 PUT false -> Session 1 GET false
        Assert.True((await Json(await a2.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());
        var putA2False = await a2.Send(HttpMethod.Put, directPath, new { isMuted = false });
        Assert.Equal(HttpStatusCode.OK, putA2False.StatusCode);
        Assert.False((await Json(putA2False)).GetProperty("isMuted").GetBoolean());
        Assert.False((await Json(await a1.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());

        // Đặt lại A = true để kiểm tra validation không làm mất trạng thái mute
        Assert.True((await Json(await a1.Send(HttpMethod.Put, directPath, new { isMuted = true }))).GetProperty("isMuted").GetBoolean());

        // 5. Validation PUT isMuted: thiếu isMuted, null, string, number, JSON lỗi -> 400 VALIDATION_ERROR và giữ nguyên isMuted = true
        foreach (var invalidRaw in new[] { "{}", "{\"other\":1}", "{\"isMuted\":null}", "{\"isMuted\":\"true\"}", "{\"isMuted\":1}", "{" })
        {
            await Error(await SendRawPutAsync(factory, a1, directPath, invalidRaw), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.True((await Json(await a1.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());
        }

        // Đặt A = false và kiểm tra payload lỗi vẫn giữ nguyên isMuted = false
        Assert.False((await Json(await a1.Send(HttpMethod.Put, directPath, new { isMuted = false }))).GetProperty("isMuted").GetBoolean());
        foreach (var invalidRaw in new[] { "{}", "{\"isMuted\":null}", "{\"isMuted\":\"false\"}" })
        {
            await Error(await SendRawPutAsync(factory, a1, directPath, invalidRaw), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.False((await Json(await a1.Send(HttpMethod.Get, directPath))).GetProperty("isMuted").GetBoolean());
        }

        // 6. Hội thoại không tồn tại -> 404 NOT_FOUND cho cả GET và PUT
        await Error(await a1.Send(HttpMethod.Get, missingPath), HttpStatusCode.NotFound, "NOT_FOUND");
        await Error(await a1.Send(HttpMethod.Put, missingPath, new { isMuted = true }), HttpStatusCode.NotFound, "NOT_FOUND");

        // 7. Quyền Direct Conversation: người ngoài và người bị đối phương chặn -> 403 CONVERSATION_ACCESS_DENIED
        await Error(await outsider.Send(HttpMethod.Get, directPath), HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");
        await Error(await outsider.Send(HttpMethod.Put, directPath, new { isMuted = true }), HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");

        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Put, $"/api/blocks/{aid}")).StatusCode);
        await Error(await a1.Send(HttpMethod.Get, directPath), HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");
        await Error(await a1.Send(HttpMethod.Put, directPath, new { isMuted = true }), HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Delete, $"/api/blocks/{aid}")).StatusCode);

        // 8. Quyền Group Conversation: thành viên hợp lệ GET/PUT 200; người ngoài và người đã rời/bị xóa -> 403 GROUP_ACCESS_DENIED
        Assert.False((await Json(await c.Send(HttpMethod.Get, groupPath))).GetProperty("isMuted").GetBoolean());
        Assert.True((await Json(await c.Send(HttpMethod.Put, groupPath, new { isMuted = true }))).GetProperty("isMuted").GetBoolean());
        Assert.False((await Json(await a1.Send(HttpMethod.Get, groupPath))).GetProperty("isMuted").GetBoolean());

        await Error(await outsider.Send(HttpMethod.Get, groupPath), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
        await Error(await outsider.Send(HttpMethod.Put, groupPath, new { isMuted = true }), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");

        // C rời nhóm -> không được GET/PUT nữa
        Assert.Equal(HttpStatusCode.NoContent, (await c.Send(HttpMethod.Post, $"/api/conversations/{groupId}/leave")).StatusCode);
        await Error(await c.Send(HttpMethod.Get, groupPath), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
        await Error(await c.Send(HttpMethod.Put, groupPath, new { isMuted = false }), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
    }

    private static async Task<HttpResponseMessage> SendRawPutAsync(
        Factory factory,
        Session session,
        string path,
        string rawJson)
    {
        var token = (await Json(await session.Send(HttpMethod.Get, "/api/auth/csrf"))).GetProperty("token").GetString()!;
        var clientField = typeof(Session).GetField("client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var httpClient = (HttpClient)clientField.GetValue(session)!;

        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await httpClient.SendAsync(request);
    }
}

