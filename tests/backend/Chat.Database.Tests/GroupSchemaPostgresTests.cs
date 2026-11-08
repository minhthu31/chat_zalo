using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public class GroupSchemaPostgresTests
{
    private static string TestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated PostgreSQL 18 test database.");
        var database = new NpgsqlConnectionStringBuilder(value).Database ?? string.Empty;
        if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Group schema tests require a dedicated _test or _review database.");
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
            throw new InvalidOperationException("Group schema tests require PostgreSQL 18.");
        }
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task SeedUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id)
    {
        var username = $"group_test_{id:N}";
        return ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "DisplayName", "CreatedAt",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{id}', '{username}', '{username.ToUpperInvariant()}', 'Group test user', now(),
                false, false, false, false, 0);
            """);
    }

    private static Task SeedGroupAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid groupId, Guid creatorId, string name = "Study group") =>
        ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{groupId}', '{creatorId}', 'Group', now(), now());
            INSERT INTO "GroupConversations" ("ConversationId", "Name") VALUES ('{groupId}', '{name}');
            """);

    private static Task AddMemberAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid groupId, Guid userId, string role) =>
        ExecuteAsync(connection, transaction, $"""
            INSERT INTO "ConversationMembers" ("ConversationId", "UserId", "Role", "JoinedAt")
            VALUES ('{groupId}', '{userId}', '{role}', now());
            """);

    [Fact]
    public async Task MigrationCreatesGroupTables_AndExistingDirectConversationStillWorks()
    {
        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, null, """
            SELECT count(*) FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261107172222_AddGroupConversationsAndAdminTransfers'
            """));

        await using var transaction = await connection.BeginTransactionAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var directId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, first);
        await SeedUserAsync(connection, transaction, second);
        var low = first.CompareTo(second) < 0 ? first : second;
        var high = first.CompareTo(second) < 0 ? second : first;
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{directId}', '{first}', 'Direct', now(), now());
            INSERT INTO "DirectConversations" ("ConversationId", "UserLowId", "UserHighId")
            VALUES ('{directId}', '{low}', '{high}');
            """);
        await SeedGroupAsync(connection, transaction, groupId, first);
        await AddMemberAsync(connection, transaction, groupId, first, "Admin");
        await AddMemberAsync(connection, transaction, groupId, second, "Member");
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
        Assert.Equal(1, await CountAsync(connection, transaction, $"SELECT count(*) FROM \"DirectConversations\" WHERE \"ConversationId\" = '{directId}'"));
        Assert.Equal(1, await CountAsync(connection, transaction, $"SELECT count(*) FROM \"GroupConversations\" WHERE \"ConversationId\" = '{groupId}'"));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ActiveGroupWithoutAdmin_IsRejectedAtTransactionBoundary()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var creator = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, creator);
        await SeedGroupAsync(connection, transaction, group, creator);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_GroupConversations_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ActiveAdminCannotLeaveWithoutTransfer()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL DEFERRED");
        await ExecuteAsync(connection, transaction,
            $"UPDATE \"ConversationMembers\" SET \"LeftAt\" = now() WHERE \"ConversationId\" = '{group}' AND \"UserId\" = '{admin}'");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversations_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GroupMetadataCannotBelongToDirectConversation()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{conversation}', '{admin}', 'Direct', now(), now());
            INSERT INTO "GroupConversations" ("ConversationId", "Name")
            VALUES ('{conversation}', 'Wrong kind');
            """);
        await AddMemberAsync(connection, transaction, conversation, admin, "Admin");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversations_Kind", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GroupCannotUseLegacyOwnerRole()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedUserAsync(connection, transaction, owner);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        await AddMemberAsync(connection, transaction, group, owner, "Owner");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversationMembers_Role", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task WhitespaceOnlyGroupName_IsRejected()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            SeedGroupAsync(connection, transaction, Guid.NewGuid(), admin, " \t "));
        Assert.Equal("CK_GroupConversations_Name", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TwoActiveAdmins_AreRejectedByUniqueIndex()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var second = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedUserAsync(connection, transaction, second);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            AddMemberAsync(connection, transaction, group, second, "Admin"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("IX_ConversationMembers_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TransferAndMemberDeparture_PreserveOneAdminAndMembershipHistory()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var oldAdmin = Guid.NewGuid();
        var newAdmin = Guid.NewGuid();
        var departingMember = Guid.NewGuid();
        var group = Guid.NewGuid();
        var request = Guid.NewGuid();
        foreach (var user in new[] { oldAdmin, newAdmin, departingMember })
            await SeedUserAsync(connection, transaction, user);
        await SeedGroupAsync(connection, transaction, group, oldAdmin);
        await AddMemberAsync(connection, transaction, group, oldAdmin, "Admin");
        await AddMemberAsync(connection, transaction, group, newAdmin, "Member");
        await AddMemberAsync(connection, transaction, group, departingMember, "Member");
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "GroupAdminTransferRequests"
                ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt")
            VALUES ('{request}', '{group}', '{oldAdmin}', '{newAdmin}', 'Pending', now());
            UPDATE "ConversationMembers" SET "Role" = 'Member'
            WHERE "ConversationId" = '{group}' AND "UserId" = '{oldAdmin}';
            UPDATE "ConversationMembers" SET "Role" = 'Admin'
            WHERE "ConversationId" = '{group}' AND "UserId" = '{newAdmin}';
            UPDATE "GroupAdminTransferRequests" SET "Status" = 'Accepted', "ResolvedAt" = now()
            WHERE "Id" = '{request}';
            UPDATE "ConversationMembers" SET "LeftAt" = now()
            WHERE "ConversationId" = '{group}' AND "UserId" = '{departingMember}';
            """);
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
        Assert.Equal(1, await CountAsync(connection, transaction,
            $"SELECT count(*) FROM \"ConversationMembers\" WHERE \"ConversationId\" = '{group}' AND \"Role\" = 'Admin' AND \"LeftAt\" IS NULL"));
        Assert.Equal(1, await CountAsync(connection, transaction,
            $"SELECT count(*) FROM \"ConversationMembers\" WHERE \"ConversationId\" = '{group}' AND \"UserId\" = '{departingMember}' AND \"LeftAt\" IS NOT NULL"));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TransferCannotHaveTwoPendingRequests()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var target = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedUserAsync(connection, transaction, target);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        await AddMemberAsync(connection, transaction, group, target, "Member");
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "GroupAdminTransferRequests" ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt")
            VALUES ('{Guid.NewGuid()}', '{group}', '{admin}', '{target}', 'Pending', now());
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, transaction, $"""
            INSERT INTO "GroupAdminTransferRequests" ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt")
            VALUES ('{Guid.NewGuid()}', '{group}', '{admin}', '{target}', 'Pending', now());
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("IX_GroupAdminTransferRequests_OnePending", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TransferTargetMustBelongToTheSameGroup()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedUserAsync(connection, transaction, outsider);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, transaction, $"""
            INSERT INTO "GroupAdminTransferRequests" ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt")
            VALUES ('{Guid.NewGuid()}', '{group}', '{admin}', '{outsider}', 'Pending', now());
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task PendingTransferCannotBeMarkedResolved()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var target = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedUserAsync(connection, transaction, target);
        await SeedGroupAsync(connection, transaction, group, admin);
        await AddMemberAsync(connection, transaction, group, admin, "Admin");
        await AddMemberAsync(connection, transaction, group, target, "Member");
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, transaction, $"""
            INSERT INTO "GroupAdminTransferRequests" ("Id", "ConversationId", "FromUserId", "ToUserId", "Status", "CreatedAt", "ResolvedAt")
            VALUES ('{Guid.NewGuid()}', '{group}', '{admin}', '{target}', 'Pending', now(), now());
            """));
        Assert.Equal("CK_GroupAdminTransferRequests_Resolution", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GroupWithoutMetadataAndAdmin_IsRejectedAtTransactionBoundary()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var user = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, user);
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{group}', '{user}', 'Group', now(), now());
            """);
        await AddMemberAsync(connection, transaction, group, user, "Member");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversations_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GroupWithoutMetadata_IsRejectedAtTransactionBoundary()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var user = Guid.NewGuid();
        var group = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, user);
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{group}', '{user}', 'Group', now(), now());
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversations_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task MovingAdminMembershipToDisbandedGroup_LeavesSourceGroupWithoutAdmin_IsRejected()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var admin = Guid.NewGuid();
        var sourceGroup = Guid.NewGuid();
        var disbandedGroup = Guid.NewGuid();
        await SeedUserAsync(connection, transaction, admin);
        await SeedGroupAsync(connection, transaction, sourceGroup, admin);
        await AddMemberAsync(connection, transaction, sourceGroup, admin, "Admin");
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "CreatedAt", "LastActivityAt")
            VALUES ('{disbandedGroup}', '{admin}', 'Group', now(), now());
            INSERT INTO "GroupConversations" ("ConversationId", "Name", "DeletedAt")
            VALUES ('{disbandedGroup}', 'Disbanded group', now());
            """);
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
        await ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL DEFERRED");
        await ExecuteAsync(connection, transaction, $"""
            UPDATE "ConversationMembers"
            SET "ConversationId" = '{disbandedGroup}'
            WHERE "ConversationId" = '{sourceGroup}' AND "UserId" = '{admin}';
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"));
        Assert.Equal("CK_GroupConversations_OneActiveAdmin", error.ConstraintName);
        await transaction.RollbackAsync();
    }
}
