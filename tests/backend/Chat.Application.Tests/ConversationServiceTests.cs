namespace Chat.Application.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Application.DTOs.Conversations;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Infrastructure.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class ConversationServiceTests
{
    private static ChatDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ChatDbContext(options);
    }

    private static ApplicationUser MakeUser(string suffix) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"u_{suffix}",
        NormalizedUserName = $"U_{suffix}",
        DisplayName = $"User {suffix}"
    };

    private class MockFriendshipService : IFriendshipService
    {
        public Func<Guid, Guid, Task<bool>>? AreFriendsHandler { get; set; }

        public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
        {
            if (AreFriendsHandler != null) return AreFriendsHandler(userAId, userBId);
            return Task.FromResult(true);
        }

        public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task AcceptAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RejectAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RemoveAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId) => throw new NotImplementedException();
        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId) => throw new NotImplementedException();
    }

    private class MockBlockService : IBlockService
    {
        public Func<Guid, Guid, Task<bool>>? IsBlockedHandler { get; set; }
        public Func<Guid, Guid, Task<bool>>? HasBlockedHandler { get; set; }

        public Task<bool> IsBlockedAsync(Guid userAId, Guid userBId)
        {
            if (IsBlockedHandler != null) return IsBlockedHandler(userAId, userBId);
            return Task.FromResult(false);
        }

        public Task<bool> HasBlockedAsync(Guid blockerUserId, Guid blockedUserId)
        {
            if (HasBlockedHandler != null) return HasBlockedHandler(blockerUserId, blockedUserId);
            return Task.FromResult(false);
        }

        public Task BlockAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task UnblockAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
    }

    // -----------------------------------------------------------------------
    // Test 1: Tạo mới thành công
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_WhenValidAndFriends_CreatesNewConversation()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");
        var userB = MakeUser("b");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.AddRange(userA, userB);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var friendship = new MockFriendshipService();
            var block = new MockBlockService();
            var service = new ConversationService(ctx, friendship, block);

            var dto = await service.CreateDirectAsync(userA.Id, userB.Id);

            Assert.NotNull(dto);
            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.Equal("Direct", dto.Kind);
            Assert.Equal(0L, dto.LastMessageSeq);
            Assert.True(dto.IsNewlyCreated);
        }

        // Verify in DB
        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var convCount = await ctx.Conversations.CountAsync();
            var directCount = await ctx.DirectConversations.CountAsync();
            var memberCount = await ctx.ConversationMembers.CountAsync();

            Assert.Equal(1, convCount);
            Assert.Equal(1, directCount);
            Assert.Equal(2, memberCount);
        }
    }

    // -----------------------------------------------------------------------
    // Test 2: Tạo trùng (idempotent, trả về IsNewlyCreated = false)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_WhenCalledTwice_ReturnsExistingConversation()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");
        var userB = MakeUser("b");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.AddRange(userA, userB);
            await ctx.SaveChangesAsync();
        }

        ConversationDto firstDto;
        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());
            firstDto = await service.CreateDirectAsync(userA.Id, userB.Id);
            Assert.True(firstDto.IsNewlyCreated);
        }

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());
            var secondDto = await service.CreateDirectAsync(userA.Id, userB.Id);

            Assert.Equal(firstDto.Id, secondDto.Id);
            Assert.False(secondDto.IsNewlyCreated);
        }

        // Verify still only 1 conversation exists
        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            Assert.Equal(1, await ctx.DirectConversations.CountAsync());
        }
    }

    // -----------------------------------------------------------------------
    // Test 3: targetUser không tồn tại (ném KeyNotFoundException "NOT_FOUND")
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_WhenTargetUserDoesNotExist_ThrowsKeyNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.Add(userA);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());
            var nonExistentTargetId = Guid.NewGuid();

            var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.CreateDirectAsync(userA.Id, nonExistentTargetId));

            Assert.Equal("NOT_FOUND", ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Test 4: Chưa kết bạn (ném UnauthorizedAccessException)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_WhenNotFriends_ThrowsUnauthorizedAccessException()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");
        var userB = MakeUser("b");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.AddRange(userA, userB);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var friendship = new MockFriendshipService
            {
                AreFriendsHandler = (_, _) => Task.FromResult(false)
            };
            var service = new ConversationService(ctx, friendship, new MockBlockService());

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.CreateDirectAsync(userA.Id, userB.Id));

            Assert.Equal("DIRECT_CONVERSATION_NOT_ALLOWED", ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Test 5: Lỗi đồng thời (Concurrent creates)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_ConcurrentCalls_ReturnConsistentConversation()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");
        var userB = MakeUser("b");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.AddRange(userA, userB);
            await ctx.SaveChangesAsync();
        }

        await using var ctx1 = CreateInMemoryDbContext(dbName);
        await using var ctx2 = CreateInMemoryDbContext(dbName);

        var service1 = new ConversationService(ctx1, new MockFriendshipService(), new MockBlockService());
        var service2 = new ConversationService(ctx2, new MockFriendshipService(), new MockBlockService());

        // Call both concurrently
        var results = await Task.WhenAll(
            service1.CreateDirectAsync(userA.Id, userB.Id),
            service2.CreateDirectAsync(userA.Id, userB.Id));

        Assert.Equal(results[0].Id, results[1].Id);

        // At most one call should report newly created
        var newlyCreatedCount = results.Count(r => r.IsNewlyCreated);
        Assert.True(newlyCreatedCount <= 1);

        // Verify only 1 record in database
        await using var verifyCtx = CreateInMemoryDbContext(dbName);
        Assert.Equal(1, await verifyCtx.DirectConversations.CountAsync());
    }

    // -----------------------------------------------------------------------
    // Additional Test: Tự chat với chính mình
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CreateDirectAsync_WhenChattingWithSelf_ThrowsUnauthorizedAccessException()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");

        await using var ctx = CreateInMemoryDbContext(dbName);
        var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CreateDirectAsync(userA.Id, userA.Id));

        Assert.Equal("DIRECT_CONVERSATION_NOT_ALLOWED", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Additional Test: GetConversationsAsync
    // -----------------------------------------------------------------------
    [Fact]
    public async Task GetConversationsAsync_ReturnsUserActiveConversations()
    {
        var dbName = Guid.NewGuid().ToString();
        var userA = MakeUser("a");
        var userB = MakeUser("b");

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            ctx.Users.AddRange(userA, userB);
            await ctx.SaveChangesAsync();

            var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());
            await service.CreateDirectAsync(userA.Id, userB.Id);
        }

        await using (var ctx = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationService(ctx, new MockFriendshipService(), new MockBlockService());
            var conversations = (await service.GetConversationsAsync(userA.Id)).ToList();

            Assert.Single(conversations);
            Assert.Equal("Direct", conversations[0].Kind);
        }
    }
}
