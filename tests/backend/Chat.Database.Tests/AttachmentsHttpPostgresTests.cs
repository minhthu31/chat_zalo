using System.Net;
using System.Net.Http.Json;
using System.Text;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chat.Database.Tests;

public partial class ConversationsHttpPostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentUploadAndDownload_RealCookiesCsrfStorageAndConversationPermissions(bool group)
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var a = new Session(factory); using var b = new Session(factory); using var c = new Session(factory);
        using var outsider = new Session(factory);
        var aid = await a.Register(); var bid = await b.Register(); var cid = await c.Register(); await outsider.Register();
        foreach (var pair in new[] { (bid, b), (cid, c) })
        {
            Assert.Equal(HttpStatusCode.Created, (await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = pair.Item1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await pair.Item2.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept")).StatusCode);
        }
        var created = group
            ? await a.Send(HttpMethod.Post, "/api/conversations/groups", new { name = "File group", memberIds = new[] { bid, cid } })
            : await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conversation = (await Json(created)).GetProperty("id").GetGuid();
        var bytes = Encoding.UTF8.GetBytes("An actual attachment, not fake storage.\n");
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonymous.BaseAddress = a.BaseAddress;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/conversations/{conversation}/attachments", new MultipartFormDataContent())).StatusCode);
        await Error(await a.Upload(conversation, bytes, csrf: false), HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Upload(conversation, bytes)).StatusCode);
        await Error(await a.Upload(conversation, bytes, field: "wrong"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await a.Upload(conversation, []), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await a.Upload(conversation, bytes, filename: "fake.png", type: "image/png"), HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE");
        // Derive the response MIME from bytes, not from the spoofed request Content-Type.
        var upload = await a.Upload(conversation, bytes, type: "image/png");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var dto = await Json(upload); var id = dto.GetProperty("id").GetGuid();
        Assert.Equal(new[] { "contentType", "contentUrl", "downloadUrl", "fileName", "id", "sizeBytes" }, dto.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("text/plain", dto.GetProperty("contentType").GetString());
        var contentUrl = dto.GetProperty("contentUrl").GetString()!;
        var downloadUrl = dto.GetProperty("downloadUrl").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(downloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Send(HttpMethod.Get, contentUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        var pending = await a.Send(HttpMethod.Get, contentUrl);
        Assert.Equal(bytes, await pending.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", pending.Headers.CacheControl!.ToString());
        var downloaded = await a.Send(HttpMethod.Get, downloadUrl);
        Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        var maximum = Enumerable.Repeat((byte)'x', 10 * 1024 * 1024).ToArray();
        Assert.Equal(HttpStatusCode.Created, (await a.Upload(conversation, maximum)).StatusCode);
        await Error(await a.Upload(conversation, new byte[10 * 1024 * 1024 + 1]), HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
        await Error(await a.Upload(conversation, new byte[12 * 1024 * 1024]), HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");

        var messageResponse = await a.Send(HttpMethod.Post, $"/api/conversations/{conversation}/messages",
            new { clientMessageId = Guid.NewGuid(), body = "A file message" });
        Assert.Equal(HttpStatusCode.Created, messageResponse.StatusCode);
        var messageId = (await Json(messageResponse)).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var file = await db.StoredFiles.SingleAsync(f => f.Id == id);
            Assert.Equal("Pending", file.Status); Assert.Equal(bytes.Length, file.SizeBytes);
            file.Status = "Attached";
            db.MessageAttachments.Add(new MessageAttachment { MessageId = messageId, StoredFileId = id });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        Assert.Equal(bytes, await (await b.Send(HttpMethod.Get, downloadUrl)).Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Put, $"/api/blocks/{bid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        if (group)
        {
            Assert.Equal(HttpStatusCode.OK, (await c.Send(HttpMethod.Get, downloadUrl)).StatusCode);
            // A blocking B does not stop B uploading/sending to the group.
            Assert.Equal(HttpStatusCode.Created, (await b.Upload(conversation, bytes)).StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Upload(conversation, bytes)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Upload(conversation, bytes)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Delete, $"/api/blocks/{bid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        if (group)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Delete, $"/api/conversations/{conversation}/members/{bid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Send(HttpMethod.Get, downloadUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Upload(conversation, bytes)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Delete, $"/api/conversations/{conversation}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Delete, $"/api/friendships/{bid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Upload(conversation, bytes)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Get, downloadUrl)).StatusCode);
        }
    }

    [Fact]
    public async Task ManualCleanup_RemovesOnlyOldUnusedPendingFiles_ProtectsAttachedAndActiveAvatar()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var a = new Session(factory); using var b = new Session(factory);
        var aid = await a.Register(); var bid = await b.Register();
        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid });
        await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");
        var conversation = (await Json(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }))).GetProperty("id").GetGuid();
        var bytes = Encoding.UTF8.GetBytes("cleanup fixture");
        var old = (await Json(await a.Upload(conversation, bytes))).GetProperty("id").GetGuid();
        var recent = (await Json(await a.Upload(conversation, bytes))).GetProperty("id").GetGuid();
        var attached = (await Json(await a.Upload(conversation, bytes))).GetProperty("id").GetGuid();
        var message = (await Json(await a.Send(HttpMethod.Post, $"/api/conversations/{conversation}/messages", new { clientMessageId = Guid.NewGuid(), body = "Keep file" }))).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            (await db.StoredFiles.SingleAsync(f => f.Id == old)).CreatedAt = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var linked = await db.StoredFiles.SingleAsync(f => f.Id == attached);
            linked.Status = "Attached"; linked.CreatedAt = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
            db.MessageAttachments.Add(new MessageAttachment { MessageId = message, StoredFileId = attached });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        var avatar = new StoredFile { UploadedByUserId = aid, UsageType = "Avatar", Status = "Active",
            StorageKey = "avatars/" + Guid.NewGuid().ToString("N"), FileName = "avatar.png", ContentType = "image/png", SizeBytes = bytes.Length };
        await storage.WriteAsync(avatar.StorageKey, new MemoryStream(bytes));
        db.StoredFiles.Add(avatar); (await db.Users.SingleAsync(u => u.Id == aid)).AvatarFileId = avatar.Id;
        await db.SaveChangesAsync();
        var oldKey = (await db.StoredFiles.SingleAsync(f => f.Id == old)).StorageKey;
        var cleaner = scope.ServiceProvider.GetRequiredService<IPendingFileCleanupService>();
        Assert.Equal(1, await cleaner.CleanupAsync(new DateTimeOffset(1995, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(await db.StoredFiles.AnyAsync(f => f.Id == old));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(oldKey));
        Assert.True(await db.StoredFiles.AnyAsync(f => f.Id == recent));
        Assert.True(await db.StoredFiles.AnyAsync(f => f.Id == attached));
        Assert.Equal(avatar.Id, (await db.Users.SingleAsync(u => u.Id == aid)).AvatarFileId);
        await using var stillThere = await storage.OpenReadAsync(avatar.StorageKey);
        Assert.Equal(bytes.Length, stillThere.Length);
    }

    [Fact]
    public async Task CleanupRechecksAFileAttachedAfterCandidateSelection_EvenWhenContextTracksOldPendingState()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var a = new Session(factory); using var b = new Session(factory);
        var aid = await a.Register(); var bid = await b.Register();
        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid });
        await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");
        var conversation = (await Json(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }))).GetProperty("id").GetGuid();
        var bytes = Encoding.UTF8.GetBytes("concurrent cleanup fixture");
        var first = (await Json(await a.Upload(conversation, bytes))).GetProperty("id").GetGuid();
        var second = (await Json(await a.Upload(conversation, bytes))).GetProperty("id").GetGuid();
        var message = (await Json(await a.Send(HttpMethod.Post, $"/api/conversations/{conversation}/messages", new { clientMessageId = Guid.NewGuid(), body = "Attach while cleanup runs" }))).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var f1 = await db.StoredFiles.SingleAsync(f => f.Id == first);
        var f2 = await db.StoredFiles.SingleAsync(f => f.Id == second);
        f1.CreatedAt = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        f2.CreatedAt = new DateTimeOffset(1981, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var intercepted = new CleanupRaceStorage(storage, async () =>
        {
            using var attaching = factory.Services.CreateScope();
            var otherDb = attaching.ServiceProvider.GetRequiredService<ChatDbContext>();
            await using var tx = await otherDb.Database.BeginTransactionAsync();
            (await otherDb.StoredFiles.SingleAsync(f => f.Id == second)).Status = "Attached";
            otherDb.MessageAttachments.Add(new MessageAttachment { MessageId = message, StoredFileId = second });
            await otherDb.SaveChangesAsync(); await tx.CommitAsync();
        });
        var cleaner = new PendingFileCleanupService(db, intercepted);
        Assert.Equal(1, await cleaner.CleanupAsync(new DateTimeOffset(1985, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Attached", (await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == second)).Status);
        await using var content = await storage.OpenReadAsync(f2.StorageKey);
        Assert.Equal(bytes.Length, content.Length);
    }


    [Fact]
    public async Task AttachmentMessages_EnforceOwnershipUsageCsvAndClientMessageIdConflict()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        using var b = new Session(factory);
        using var c = new Session(factory);

        var aid = await a.Register();
        var bid = await b.Register();
        var cid = await c.Register();

        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid });
        await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");

        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = cid });
        await c.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");

        var conversation = (await Json(await a.Send(
            HttpMethod.Post,
            "/api/conversations/direct",
            new { targetUserId = bid }))).GetProperty("id").GetGuid();

        var otherConversation = (await Json(await a.Send(
            HttpMethod.Post,
            "/api/conversations/direct",
            new { targetUserId = cid }))).GetProperty("id").GetGuid();

        var bytes = Encoding.UTF8.GetBytes("attachment test");

        var firstFile = (await Json(await a.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var secondFile = (await Json(await a.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var fileOwnedByB = (await Json(await b.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var fileFromOtherConversation = (await Json(await a.Upload(otherConversation, bytes)))
            .GetProperty("id").GetGuid();

        var firstClientMessageId = Guid.NewGuid();

        var firstMessageResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = firstClientMessageId,
                body = "First attachment",
                attachmentIds = new[] { firstFile }
            });

        Assert.Equal(HttpStatusCode.Created, firstMessageResponse.StatusCode);

        var firstMessageId = (await Json(firstMessageResponse))
            .GetProperty("message").GetProperty("id").GetGuid();

        var secondMessageResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Second attachment",
                attachmentIds = new[] { secondFile }
            });

        Assert.Equal(HttpStatusCode.Created, secondMessageResponse.StatusCode);

        var secondMessageId = (await Json(secondMessageResponse))
            .GetProperty("message").GetProperty("id").GetGuid();

        var detailsResponse = await a.Send(
            HttpMethod.Get,
            $"/api/conversations/{conversation}/messages/attachment-details?messageIds={firstMessageId},{secondMessageId}");

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        var details = await Json(detailsResponse);
        Assert.Equal(2, details.GetArrayLength());

        // Duplicate IDs must be removed while preserving first-seen order.
        var duplicateDetailsResponse = await a.Send(
            HttpMethod.Get,
            $"/api/conversations/{conversation}/messages/attachment-details?messageIds={secondMessageId},{firstMessageId},{secondMessageId}");

        Assert.Equal(HttpStatusCode.OK, duplicateDetailsResponse.StatusCode);

        var duplicateDetails = await Json(duplicateDetailsResponse);
        Assert.Equal(2, duplicateDetails.GetArrayLength());
        Assert.Equal(
            secondMessageId,
            duplicateDetails[0].GetProperty("messageId").GetGuid());
        Assert.Equal(
            firstMessageId,
            duplicateDetails[1].GetProperty("messageId").GetGuid());

        // Missing messageIds.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        // Empty messageIds.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details?messageIds="),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        // Invalid GUID.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details?messageIds=not-a-guid"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        // Empty GUID.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details?messageIds={Guid.Empty}"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        // More than 100 IDs.
        var tooManyIds = string.Join(
            ",",
            Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));

        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details?messageIds={tooManyIds}"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");

        // Nonexistent message.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{conversation}/messages/attachment-details?messageIds={Guid.NewGuid()}"),
            HttpStatusCode.NotFound,
            "NOT_FOUND");

        // Existing message belonging to another conversation.
        await Error(
            await a.Send(
                HttpMethod.Get,
                $"/api/conversations/{otherConversation}/messages/attachment-details?messageIds={firstMessageId}"),
            HttpStatusCode.Forbidden,
            "CONVERSATION_ACCESS_DENIED");


        var reusedFileResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Reuse attachment",
                attachmentIds = new[] { firstFile }
            });

        await Error(reusedFileResponse, HttpStatusCode.Conflict, "ATTACHMENT_ALREADY_USED");
        var otherUsersFileResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Wrong owner",
                attachmentIds = new[] { fileOwnedByB }
            });
        await Error(otherUsersFileResponse, HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");

        var otherConversationFileResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Wrong conversation",
                attachmentIds = new[] { fileFromOtherConversation }
            });

        await Error(otherConversationFileResponse, HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");

        var textWithSameClientMessageId = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages",
            new
            {
                clientMessageId = firstClientMessageId,
                body = "Text instead of attachment"
            });

        await Error(textWithSameClientMessageId, HttpStatusCode.Conflict, "CLIENT_MESSAGE_ID_CONFLICT");
    }

    [Fact]
    public async Task AttachmentDetails_RejectsEntireRequestWhenOneGroupMessageIsHiddenByBlock()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        using var b = new Session(factory);
        using var c = new Session(factory);

        var aid = await a.Register();
        var bid = await b.Register();
        var cid = await c.Register();

        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid });
        await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");

        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = cid });
        await c.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");

        var conversation = (await Json(await a.Send(
            HttpMethod.Post,
            "/api/conversations/groups",
            new
            {
                name = "Attachment visibility test",
                memberIds = new[] { bid, cid }
            }))).GetProperty("id").GetGuid();

        var bytes = Encoding.UTF8.GetBytes("group attachment test");

        var aFile = (await Json(await a.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var bFile = (await Json(await b.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var aMessageResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Message from A",
                attachmentIds = new[] { aFile }
            });

        Assert.Equal(HttpStatusCode.Created, aMessageResponse.StatusCode);

        var aMessageId = (await Json(aMessageResponse))
            .GetProperty("message").GetProperty("id").GetGuid();

        var bMessageResponse = await b.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = "Message from B",
                attachmentIds = new[] { bFile }
            });

        Assert.Equal(HttpStatusCode.Created, bMessageResponse.StatusCode);

        var bMessageId = (await Json(bMessageResponse))
            .GetProperty("message").GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await a.Send(HttpMethod.Put, $"/api/blocks/{cid}")).StatusCode);

        var visibleOnlyResponse = await c.Send(
            HttpMethod.Get,
            $"/api/conversations/{conversation}/messages/attachment-details?messageIds={bMessageId}");

        Assert.Equal(HttpStatusCode.OK, visibleOnlyResponse.StatusCode);

        var mixedResponse = await c.Send(
            HttpMethod.Get,
            $"/api/conversations/{conversation}/messages/attachment-details?messageIds={aMessageId},{bMessageId}");

        await Error(mixedResponse, HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");
    }


    [Fact]
    public async Task AttachmentMessages_ValidateAttachmentIdsAndPreserveRetryOrder()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        using var a = new Session(factory);
        using var b = new Session(factory);

        var aid = await a.Register();
        var bid = await b.Register();

        await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid });
        await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept");

        var conversation = (await Json(await a.Send(
            HttpMethod.Post,
            "/api/conversations/direct",
            new { targetUserId = bid }))).GetProperty("id").GetGuid();

        var bytes = Encoding.UTF8.GetBytes("attachment validation test");

        var firstFile = (await Json(await a.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var secondFile = (await Json(await a.Upload(conversation, bytes)))
            .GetProperty("id").GetGuid();

        var clientMessageId = Guid.NewGuid();

        await Error(
            await a.Send(HttpMethod.Post,
                $"/api/conversations/{conversation}/messages/attachments",
                new { clientMessageId, body = "Missing IDs" }),
            HttpStatusCode.BadRequest,
            "INVALID_ATTACHMENT_COUNT");

        await Error(
            await a.Send(HttpMethod.Post,
                $"/api/conversations/{conversation}/messages/attachments",
                new { clientMessageId = Guid.NewGuid(), body = "Empty IDs", attachmentIds = Array.Empty<Guid>() }),
            HttpStatusCode.BadRequest,
            "INVALID_ATTACHMENT_COUNT");

        await Error(
            await a.Send(HttpMethod.Post,
                $"/api/conversations/{conversation}/messages/attachments",
                new { clientMessageId = Guid.NewGuid(), body = "Duplicate IDs", attachmentIds = new[] { firstFile, firstFile } }),
            HttpStatusCode.BadRequest,
            "INVALID_ATTACHMENT_COUNT");

        await Error(
            await a.Send(HttpMethod.Post,
                $"/api/conversations/{conversation}/messages/attachments",
                new
                {
                    clientMessageId = Guid.NewGuid(),
                    body = "Too many IDs",
                    attachmentIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray()
                }),
            HttpStatusCode.BadRequest,
            "INVALID_ATTACHMENT_COUNT");

        var request = new
        {
            clientMessageId,
            body = "Retry order test",
            attachmentIds = new[] { secondFile, firstFile }
        };

        var firstResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var firstResult = await Json(firstResponse);
        var firstOrder = firstResult.GetProperty("attachments")
            .EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToArray();

        Assert.Equal(firstOrder.OrderBy(id => id), firstOrder);

        var retryResponse = await a.Send(
            HttpMethod.Post,
            $"/api/conversations/{conversation}/messages/attachments",
            request);

        Assert.True(retryResponse.IsSuccessStatusCode);

        var retryResult = await Json(retryResponse);
        var retryOrder = retryResult.GetProperty("attachments")
            .EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToArray();

        Assert.Equal(firstOrder, retryOrder);
    }

    private sealed class CleanupRaceStorage(IFileStorage inner, Func<Task> beforeFirstDelete) : IFileStorage
    {
        private bool first = true;
        public Task WriteAsync(string key, Stream content, CancellationToken ct = default) => inner.WriteAsync(key, content, ct);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => inner.OpenReadAsync(key, ct);
        public async Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (first) { first = false; await beforeFirstDelete(); }
            await inner.DeleteAsync(key, ct);
        }
    }
}
