using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

/// <summary>
/// S2-09: kiểm tra GroupPermissionService trên PostgreSQL thật, gồm thay đổi quyền đồng thời.
/// Cần CHAT_TEST_CONNECTION_STRING trỏ tới database PostgreSQL 18 đã migrate, tên chứa _test hoặc _review.
/// Dữ liệu được commit thật (nhiều kết nối song song) nên mỗi test tự dọn dữ liệu của mình.
/// </summary>
public class GroupPermissionPostgresTests
{
    private sealed record Fixture(Guid GroupId, Guid AdminId, Guid MemberId, Guid Member2Id);

    private static string TestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated PostgreSQL 18 test database.");
        var database = new NpgsqlConnectionStringBuilder(value).Database ?? string.Empty;
        if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Group permission tests require a dedicated _test or _review database.");
        }
        return value;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(TestConnectionString());
        await connection.OpenAsync();
        if (connection.PostgreSqlVersion.Major != 18)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("Group permission tests require PostgreSQL 18.");
        }
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task SeedUserAsync(NpgsqlConnection connection, Guid id)
    {
        var username = $"group_perm_{id:N}";
        return ExecuteAsync(connection, $"""
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "DisplayName", "CreatedAt",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{id}', '{username}', '{username.ToUpperInvariant()}', 'Group perm user', now(),
                false, false, false, false, 0);
            """);
    }

    private static async Task<Fixture> SeedGroupAsync(NpgsqlConnection connection)
    {
        var f = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var id in new[] { f.AdminId, f.MemberId, f.Member2Id })
            await SeedUserAsync(connection, id);

        await ExecuteAsync(connection, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{f.GroupId}', '{f.AdminId}', 'Group', now(), now());
            INSERT INTO "GroupConversations" ("ConversationId", "Name")
            VALUES ('{f.GroupId}', 'Permission test group');
            INSERT INTO "ConversationMembers" ("ConversationId", "UserId", "Role", "JoinedAt")
            VALUES ('{f.GroupId}', '{f.AdminId}', 'Admin', now()),
                   ('{f.GroupId}', '{f.MemberId}', 'Member', now()),
                   ('{f.GroupId}', '{f.Member2Id}', 'Member', now());
            """);
        return f;
    }

    private static Task CleanupAsync(NpgsqlConnection connection, Fixture f) => ExecuteAsync(connection, $"""
        DELETE FROM "ConversationMembers" WHERE "ConversationId" = '{f.GroupId}';
        DELETE FROM "GroupConversations" WHERE "ConversationId" = '{f.GroupId}';
        DELETE FROM "Conversations" WHERE "Id" = '{f.GroupId}';
        DELETE FROM "Users" WHERE "Id" IN ('{f.AdminId}', '{f.MemberId}', '{f.Member2Id}');
        """);

    // DbContext không thread-safe: mỗi lần kiểm tra dùng một context riêng.
    private static ChatDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(TestConnectionString()).Options);

    // Các test này không gọi tới kiểm tra bạn bè nên truyền null cho IFriendshipService.
    private static GroupPermissionService NewService(ChatDbContext db) =>
        new(new GroupMembershipQuery(db), null!);

    [Fact]
    public async Task ActiveMembers_AndAdmin_AreRecognizedFromRealSchema()
    {
        await using var setup = await OpenAsync();
        var f = await SeedGroupAsync(setup);
        try
        {
            await using var db = NewContext();
            var sut = NewService(db);

            Assert.Equal(GroupRole.Admin, await sut.GetRoleAsync(f.GroupId, f.AdminId));
            Assert.Equal(GroupRole.Member, await sut.GetRoleAsync(f.GroupId, f.MemberId));
            Assert.True((await sut.CanSendMessageAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.True((await sut.CanJoinRealtimeAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.False((await sut.CanDisbandGroupAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.True((await sut.CanDisbandGroupAsync(f.GroupId, f.AdminId)).Allowed);
            Assert.False((await sut.CanSendMessageAsync(f.GroupId, Guid.NewGuid())).Allowed);
        }
        finally { await CleanupAsync(setup, f); }
    }

    [Fact]
    public async Task DisbandedGroup_CannotBeJoinedOrWrittenTo_AndLeavesRealtimeList()
    {
        await using var setup = await OpenAsync();
        var f = await SeedGroupAsync(setup);
        try
        {
            await ExecuteAsync(setup, $"""UPDATE "GroupConversations" SET "DeletedAt" = now() WHERE "ConversationId" = '{f.GroupId}';""");

            await using var db = NewContext();
            var sut = NewService(db);

            var join = await sut.CanJoinRealtimeAsync(f.GroupId, f.AdminId);
            Assert.False(join.Allowed);
            Assert.Equal("GROUP_ACCESS_DENIED", join.ErrorCode);
            Assert.Equal(403, join.HttpStatus);
            Assert.False((await sut.CanSendMessageAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.False((await sut.CanViewMessagesAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.DoesNotContain(f.GroupId, await sut.GetRealtimeGroupIdsAsync(f.MemberId));
        }
        finally { await CleanupAsync(setup, f); }
    }

    [Fact]
    public async Task MemberRemovedWhileChecksRun_IsDeniedAfterCommit()
    {
        await using var setup = await OpenAsync();
        var f = await SeedGroupAsync(setup);
        try
        {
            var checks = Enumerable.Range(0, 60).Select(async i =>
            {
                if (i == 30)
                    await ExecuteAsync(setup, $"""UPDATE "ConversationMembers" SET "LeftAt" = now() WHERE "ConversationId" = '{f.GroupId}' AND "UserId" = '{f.MemberId}';""");

                await using var db = NewContext();
                return await NewService(db).CanSendMessageAsync(f.GroupId, f.MemberId);
            }).ToArray();

            var results = await Task.WhenAll(checks);

            // Trong lúc đổi quyền: mỗi kết quả hoặc được phép, hoặc bị từ chối rõ ràng; không lỗi, không trạng thái nửa vời.
            Assert.All(results, r => Assert.True(r.Allowed || r.ErrorCode == "GROUP_ACCESS_DENIED"));

            // Sau khi commit: mọi kiểm tra mới đều bị từ chối.
            var after = await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
            {
                await using var db = NewContext();
                var sut = NewService(db);
                return (Send: await sut.CanSendMessageAsync(f.GroupId, f.MemberId),
                        Join: await sut.CanJoinRealtimeAsync(f.GroupId, f.MemberId));
            }));
            Assert.All(after, r =>
            {
                Assert.False(r.Send.Allowed);
                Assert.False(r.Join.Allowed);
            });
        }
        finally { await CleanupAsync(setup, f); }
    }

    [Fact]
    public async Task AdminTransferDuringChecks_EndsWithExactlyOneAdmin_AndNewRightsApply()
    {
        await using var setup = await OpenAsync();
        var f = await SeedGroupAsync(setup);
        try
        {
            var transfer = Task.Run(async () =>
            {
                await using var connection = await OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await ExecuteAsync(connection, $"""UPDATE "ConversationMembers" SET "Role" = 'Member' WHERE "ConversationId" = '{f.GroupId}' AND "UserId" = '{f.AdminId}';""", transaction);
                await ExecuteAsync(connection, $"""UPDATE "ConversationMembers" SET "Role" = 'Admin' WHERE "ConversationId" = '{f.GroupId}' AND "UserId" = '{f.MemberId}';""", transaction);
                await Task.Delay(50);
                await transaction.CommitAsync();
            });

            var checks = Enumerable.Range(0, 40).Select(async _ =>
            {
                await using var db = NewContext();
                var sut = NewService(db);
                return (Old: await sut.CanDisbandGroupAsync(f.GroupId, f.AdminId),
                        New: await sut.CanDisbandGroupAsync(f.GroupId, f.MemberId));
            }).ToArray();

            await Task.WhenAll(checks.Cast<Task>().Append(transfer));

            // Luôn đúng một Admin đang hoạt động (ràng buộc IX_ConversationMembers_OneActiveAdmin).
            Assert.Equal(1, await CountAsync(setup, $"""SELECT count(*) FROM "ConversationMembers" WHERE "ConversationId" = '{f.GroupId}' AND "Role" = 'Admin' AND "LeftAt" IS NULL;"""));

            // Sau khi chuyển quyền: Admin cũ mất quyền, Admin mới có quyền.
            await using var db = NewContext();
            var final = NewService(db);
            Assert.False((await final.CanDisbandGroupAsync(f.GroupId, f.AdminId)).Allowed);
            Assert.True((await final.CanDisbandGroupAsync(f.GroupId, f.MemberId)).Allowed);
            Assert.True((await final.CanProposeAdminTransferAsync(f.GroupId, f.MemberId, f.Member2Id)).Allowed);
            Assert.False((await final.CanProposeAdminTransferAsync(f.GroupId, f.AdminId, f.Member2Id)).Allowed);
        }
        finally { await CleanupAsync(setup, f); }
    }
}