using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

/// <summary>
/// Kiểm tra Race Condition khi xử lý đồng thời Accept và Reject trên cùng một GroupAdminTransferRequest
/// sử dụng Pessimistic Locking (SELECT ... FOR UPDATE) trên PostgreSQL thật.
/// </summary>
public class GroupAdminTransferConcurrencyTests
{
    private sealed record Fixture(Guid GroupId, Guid AdminId, Guid NomineeId, Guid RequestId);

    private static string TestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated PostgreSQL 18 test database.");
        var database = new NpgsqlConnectionStringBuilder(value).Database ?? string.Empty;
        if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Group concurrency tests require a dedicated _test or _review database.");
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
            throw new InvalidOperationException("Group concurrency tests require PostgreSQL 18.");
        }
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static Task SeedUserAsync(NpgsqlConnection connection, Guid id)
    {
        var username = $"transfer_concurrent_{id:N}";
        return ExecuteAsync(connection, $"""
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "DisplayName", "CreatedAt",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{id}', '{username}', '{username.ToUpperInvariant()}', 'Concurrent transfer user', now(),
                false, false, false, false, 0);
            """);
    }

    private static async Task<Fixture> SeedFixtureAsync(NpgsqlConnection connection)
    {
        var f = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedUserAsync(connection, f.AdminId);
        await SeedUserAsync(connection, f.NomineeId);

        await ExecuteAsync(connection, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{f.GroupId}', '{f.AdminId}', 'Group', now(), now());

            INSERT INTO "GroupConversations" ("ConversationId", "Name")
            VALUES ('{f.GroupId}', 'Concurrent Transfer Test Group');

            INSERT INTO "ConversationMembers" ("ConversationId", "UserId", "Role", "JoinedAt")
            VALUES ('{f.GroupId}', '{f.AdminId}', 'Admin', now()),
                   ('{f.GroupId}', '{f.NomineeId}', 'Member', now());

            INSERT INTO "GroupAdminTransferRequests" ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt", "ResolvedAt")
            VALUES ('{f.RequestId}', '{f.GroupId}', '{f.AdminId}', '{f.NomineeId}', 'Pending', now(), NULL);
            """);

        return f;
    }

    private static Task CleanupAsync(NpgsqlConnection connection, Fixture f) => ExecuteAsync(connection, $"""
        DELETE FROM "GroupAdminTransferRequests" WHERE "ConversationId" = '{f.GroupId}';
        DELETE FROM "ConversationMembers" WHERE "ConversationId" = '{f.GroupId}';
        DELETE FROM "GroupConversations" WHERE "ConversationId" = '{f.GroupId}';
        DELETE FROM "Conversations" WHERE "Id" = '{f.GroupId}';
        DELETE FROM "Users" WHERE "Id" IN ('{f.AdminId}', '{f.NomineeId}');
        """);

    private static ChatDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(TestConnectionString()).Options);

    [Fact]
    public async Task ConcurrentAcceptAndReject_ResolvesExactlyOnce_OtherFailsWith409()
    {
        await using var setup = await OpenAsync();
        var f = await SeedFixtureAsync(setup);

        try
        {
            // Khởi tạo 2 DbContext độc lập và 2 service riêng biệt
            await using var db1 = NewContext();
            await using var db2 = NewContext();

            var service1 = new GroupService(db1, null!, null!);
            var service2 = new GroupService(db2, null!, null!);

            // Mô phỏng gọi AcceptAsync và RejectAsync ĐỒNG THỜI
            var acceptTask = Task.Run(async () =>
            {
                try
                {
                    await service1.AcceptAdminTransferAsync(f.NomineeId, f.GroupId, f.RequestId);
                    return (Action: "Accept", Success: true, Exception: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Action: "Accept", Success: false, Exception: (Exception?)ex);
                }
            });

            var rejectTask = Task.Run(async () =>
            {
                try
                {
                    await service2.RejectAdminTransferAsync(f.NomineeId, f.GroupId, f.RequestId);
                    return (Action: "Reject", Success: true, Exception: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Action: "Reject", Success: false, Exception: (Exception?)ex);
                }
            });

            var results = await Task.WhenAll(acceptTask, rejectTask);

            // Assert 1: Chỉ có đúng MỘT thao tác thành công (200/NoContent), thao tác còn lại phải bị văng lỗi/trả về 409 ADMIN_TRANSFER_ALREADY_RESOLVED
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);
            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);

            var failedResult = results.Single(r => !r.Success);
            var winningResult = results.Single(r => r.Success);

            var gex = Assert.IsType<GroupServiceException>(failedResult.Exception);
            Assert.Equal(409, gex.StatusCode);
            Assert.Equal("ADMIN_TRANSFER_ALREADY_RESOLVED", gex.Code);
            Assert.Equal("Yêu cầu này đã được xử lý.", gex.Message);

            // Assert 2: Kiểm tra lại Database sau khi chạy xong: Status của request và Role của 2 user trong ConversationMembers
            // phải nhất quán và khớp với thao tác chiến thắng
            await using var verifyDb = NewContext();
            var finalRequest = await verifyDb.GroupAdminTransferRequests.SingleAsync(r => r.Id == f.RequestId);
            var adminMember = await verifyDb.ConversationMembers.SingleAsync(m => m.ConversationId == f.GroupId && m.UserId == f.AdminId);
            var nomineeMember = await verifyDb.ConversationMembers.SingleAsync(m => m.ConversationId == f.GroupId && m.UserId == f.NomineeId);

            if (winningResult.Action == "Accept")
            {
                Assert.Equal("Accepted", finalRequest.Status);
                Assert.NotNull(finalRequest.ResolvedAt);
                Assert.Equal("Member", adminMember.Role);
                Assert.Equal("Admin", nomineeMember.Role);
            }
            else
            {
                Assert.Equal("Rejected", finalRequest.Status);
                Assert.NotNull(finalRequest.ResolvedAt);
                Assert.Equal("Admin", adminMember.Role);
                Assert.Equal("Member", nomineeMember.Role);
            }
        }
        finally
        {
            await CleanupAsync(setup, f);
        }
    }
}
