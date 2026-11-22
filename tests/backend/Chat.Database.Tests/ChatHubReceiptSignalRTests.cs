using System.Text.Json;
using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chat.Database.Tests;

public sealed partial class ChatHubSignalRTests
{
    [Fact]
    public async Task MessageReceiptChanged_IsSentOnlyToJoinedAuthorizedGroupMembers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ownerId = await database.AddUserAsync();
        var blockedMemberId = await database.AddUserAsync();
        var visibleMemberId = await database.AddUserAsync();
        var lateMemberId = await database.AddUserAsync();
        var unjoinedMemberId = await database.AddUserAsync();
        var conversationId = await database.AddGroupConversationAsync(
            ownerId,
            (ownerId, null),
            (blockedMemberId, null),
            (visibleMemberId, null),
            (unjoinedMemberId, null),
            (lateMemberId, null));

        using var factory = CreateFactory();

        var messageId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var conversation = await db.Conversations.SingleAsync(c => c.Id == conversationId);
            conversation.LastMessageSeq = 2;
            var lateMember = await db.ConversationMembers
                .SingleAsync(m => m.ConversationId == conversationId && m.UserId == lateMemberId);
            lateMember.JoinedSeq = 2;
            db.Messages.Add(new Message
            {
                Id = messageId, ConversationId = conversationId, SenderUserId = ownerId,
                ClientMessageId = Guid.NewGuid(), Seq = 1, Body = "Before joining",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.UserBlocks.Add(new UserBlock
            {
                BlockerUserId = ownerId,
                BlockedUserId = blockedMemberId
            });
            await db.SaveChangesAsync();

            var receipts = scope.ServiceProvider.GetRequiredService<IMessageReceiptService>();
            var lateReceipts = await receipts.GetAsync(lateMemberId, conversationId);
            Assert.Empty(lateReceipts.Messages);
        }

        await using var ownerConnection = CreateConnection(factory, ownerId);
        await using var blockedConnection = CreateConnection(factory, blockedMemberId);
        await using var visibleConnection = CreateConnection(factory, visibleMemberId);
        await using var lateConnection = CreateConnection(factory, lateMemberId);
        await using var unjoinedConnection = CreateConnection(factory, unjoinedMemberId);

        await ownerConnection.StartAsync();
        await blockedConnection.StartAsync();
        await visibleConnection.StartAsync();
        await lateConnection.StartAsync();
        await unjoinedConnection.StartAsync();

        await ownerConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await blockedConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await visibleConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await lateConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        // unjoinedConnection intentionally remains connected but unsubscribed.

        var ownerReceived = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var visibleReceived = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedEvents = 0;
        var unjoinedEvents = 0;
        var lateEvents = 0;

        ownerConnection.On<JsonElement>("MessageReceiptChanged", payload =>
            ownerReceived.TrySetResult(payload));
        visibleConnection.On<JsonElement>("MessageReceiptChanged", payload =>
            visibleReceived.TrySetResult(payload));
        blockedConnection.On<JsonElement>("MessageReceiptChanged", _ =>
            Interlocked.Increment(ref blockedEvents));
        unjoinedConnection.On<JsonElement>("MessageReceiptChanged", _ =>
            Interlocked.Increment(ref unjoinedEvents));

        lateConnection.On<JsonElement>("MessageReceiptChanged", _ =>
            Interlocked.Increment(ref lateEvents));

        using (var scope = factory.Services.CreateScope())
        {
            var receipts = scope.ServiceProvider.GetRequiredService<IMessageReceiptService>();
            await receipts.MarkReadAsync(visibleMemberId, conversationId, new UpdateReceiptRequest { Seq = 1 });
        }

        var ownerPayload = await ownerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var visiblePayload = await visibleReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(conversationId, ownerPayload.GetProperty("conversationId").GetGuid());
        Assert.Equal(visibleMemberId, ownerPayload.GetProperty("userId").GetGuid());
        Assert.Equal(messageId, ownerPayload.GetProperty("messageId").GetGuid());
        Assert.Equal(1L, ownerPayload.GetProperty("seq").GetInt64());
        Assert.True(ownerPayload.GetProperty("isDelivered").GetBoolean());
        Assert.True(ownerPayload.GetProperty("isRead").GetBoolean());
        Assert.Equal(conversationId, visiblePayload.GetProperty("conversationId").GetGuid());

        await WaitForNoReceiptEventAsync(() => Volatile.Read(ref blockedEvents));
        await WaitForNoReceiptEventAsync(() => Volatile.Read(ref unjoinedEvents));
        Assert.Equal(0, Volatile.Read(ref lateEvents));
    }

    private static async Task WaitForNoReceiptEventAsync(Func<int> getCount)
    {
        await Task.Delay(500);
        Assert.Equal(0, getCount());
    }
}
