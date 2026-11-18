using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
namespace Chat.Database.Tests;
public partial class ConversationsHttpPostgresTests
{
    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task PendingAdminTransfers_VisibleOnlyToParticipants_AndRemovedAfterResolution(string action)
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var a = new Session(factory); using var b = new Session(factory);
        using var c = new Session(factory); using var outsider = new Session(factory);
        var aid = await a.Register(); var bid = await b.Register();
        var cid = await c.Register(); await outsider.Register();
        foreach (var target in new[] { bid, cid })
        {
            Assert.Equal(HttpStatusCode.Created, (await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = target })).StatusCode);
            var recipient = target == bid ? b : c;
            Assert.Equal(HttpStatusCode.NoContent, (await recipient.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept")).StatusCode);
        }
        var groupResponse = await a.Send(HttpMethod.Post, "/api/conversations/groups", new { name = "Pending transfer", memberIds = new[] { bid, cid } });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var groupId = (await Json(groupResponse)).GetProperty("id").GetGuid();
        var path = $"/api/conversations/{groupId}/admin-transfer";
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonymous.BaseAddress = a.BaseAddress;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Empty((await Json(await b.Send(HttpMethod.Get, path))).EnumerateArray());
        var proposed = await a.Send(HttpMethod.Post, path, new { targetUserId = bid });
        Assert.Equal(HttpStatusCode.Accepted, proposed.StatusCode);
        var requestId = (await Json(proposed)).GetProperty("id").GetGuid();
        foreach (var participant in new[] { a, b })
        {
            // GET works without a CSRF token, using a real Identity cookie.
            var response = await participant.Send(HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var item = Assert.Single((await Json(response)).EnumerateArray());
            Assert.Equal(requestId, item.GetProperty("id").GetGuid());
            Assert.Equal(bid, item.GetProperty("toUserId").GetGuid());
            Assert.Equal("Pending", item.GetProperty("status").GetString());
            Assert.Equal(new[] { "conversationId", "createdAt", "fromUserId", "id", "status", "toUserId" }, item.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        }
        Assert.Single((await Json(await b.Send(HttpMethod.Get, $"/api/groups/{groupId}/admin-transfer"))).EnumerateArray());
        Assert.Empty((await Json(await c.Send(HttpMethod.Get, path))).EnumerateArray());
        await Error(await outsider.Send(HttpMethod.Get, path), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Post, $"{path}/{requestId}/{action}")).StatusCode);
        Assert.Empty((await Json(await a.Send(HttpMethod.Get, path))).EnumerateArray());
        Assert.Empty((await Json(await b.Send(HttpMethod.Get, path))).EnumerateArray());
        var admin = action == "accept" ? b : a;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Send(HttpMethod.Delete, $"/api/conversations/{groupId}/members/{cid}")).StatusCode);
        await Error(await c.Send(HttpMethod.Get, path), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Send(HttpMethod.Delete, $"/api/conversations/{groupId}")).StatusCode);
        await Error(await admin.Send(HttpMethod.Get, path), HttpStatusCode.Forbidden, "GROUP_ACCESS_DENIED");
    }
}
