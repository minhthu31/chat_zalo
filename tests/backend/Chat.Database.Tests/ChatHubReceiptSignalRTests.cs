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
        var unjoinedMemberId = await database.AddUserAsync();
        var conversationId = await database.AddGroupConversationAsync(
            ownerId,
            (ownerId, null),
            (blockedMemberId, null),
            (visibleMemberId, null),
            (unjoinedMemberId, null));

        using var factory = CreateFactory();

        // The receipt owner has blocked this member. Existing Group history
        // hides the owner's messages from this member, so it must not receive
        // that owner's cursor either.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            db.UserBlocks.Add(new UserBlock
            {
                BlockerUserId = ownerId,
                BlockedUserId = blockedMemberId
            });
            await db.SaveChangesAsync();
        }

        await using var ownerConnection = CreateConnection(factory, ownerId);
        await using var blockedConnection = CreateConnection(factory, blockedMemberId);
        await using var visibleConnection = CreateConnection(factory, visibleMemberId);
        await using var unjoinedConnection = CreateConnection(factory, unjoinedMemberId);

        await ownerConnection.StartAsync();
        await blockedConnection.StartAsync();
        await visibleConnection.StartAsync();
        await unjoinedConnection.StartAsync();

        await ownerConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await blockedConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await visibleConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        // unjoinedConnection intentionally remains connected but unsubscribed.

        var ownerReceived = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var visibleReceived = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedEvents = 0;
        var unjoinedEvents = 0;

        ownerConnection.On<JsonElement>("MessageReceiptChanged", payload =>
            ownerReceived.TrySetResult(payload));
        visibleConnection.On<JsonElement>("MessageReceiptChanged", payload =>
            visibleReceived.TrySetResult(payload));
        blockedConnection.On<JsonElement>("MessageReceiptChanged", _ =>
            Interlocked.Increment(ref blockedEvents));
        unjoinedConnection.On<JsonElement>("MessageReceiptChanged", _ =>
            Interlocked.Increment(ref unjoinedEvents));

        var receipt = new MessageReceiptChangedDto
        {
            ConversationId = conversationId,
            UserId = ownerId,
            DeliveredSeq = 7,
            ReadSeq = 5
        };

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IReceiptEventPublisher>()
                .PublishAsync(receipt);
        }

        var ownerPayload = await ownerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var visiblePayload = await visibleReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(conversationId, ownerPayload.GetProperty("conversationId").GetGuid());
        Assert.Equal(ownerId, ownerPayload.GetProperty("userId").GetGuid());
        Assert.Equal(7L, ownerPayload.GetProperty("deliveredSeq").GetInt64());
        Assert.Equal(5L, ownerPayload.GetProperty("readSeq").GetInt64());
        Assert.Equal(conversationId, visiblePayload.GetProperty("conversationId").GetGuid());

        await WaitForNoReceiptEventAsync(() => Volatile.Read(ref blockedEvents));
        await WaitForNoReceiptEventAsync(() => Volatile.Read(ref unjoinedEvents));
    }

    private static async Task WaitForNoReceiptEventAsync(Func<int> getCount)
    {
        await Task.Delay(500);
        Assert.Equal(0, getCount());
    }
}
