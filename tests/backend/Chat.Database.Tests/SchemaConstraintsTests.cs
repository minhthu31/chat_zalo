namespace Chat.Database.Tests;

using Npgsql;
using Xunit;

public class SchemaConstraintsTests
{
    // Fixed IDs live only inside a transaction rolled back after each case.
    private const string Low = "00000000-0000-0000-0000-000000000001";
    private const string High = "00000000-0000-0000-0000-000000000002";

    [Theory]
    [InlineData("INSERT INTO \"UserBlocks\" (\"BlockerUserId\", \"BlockedUserId\", \"CreatedAt\") VALUES ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000099', now())", "23503", "FK_UserBlocks_Users_BlockedUserId")]
    [InlineData("UPDATE \"Users\" SET \"NormalizedEmail\"='SCHEMA_LOW@TEST' WHERE \"Id\"='00000000-0000-0000-0000-000000000002'", "23505", "EmailIndex")]
    [InlineData("UPDATE \"Friendships\" SET \"RequesterUserId\"='00000000-0000-0000-0000-000000000003'", "23514", "CK_Friendships_RequesterUserId")]
    [InlineData("UPDATE \"Friendships\" SET \"Status\"='Unknown'", "23514", "CK_Friendships_Status")]
    [InlineData("UPDATE \"Conversations\" SET \"LastMessageSeq\"=-1", "23514", "CK_Conversations_LastMessageSeq")]
    [InlineData("UPDATE \"Conversations\" SET \"Kind\"='Unknown'", "23514", "CK_Conversations_Kind")]
    [InlineData("UPDATE \"ConversationMembers\" SET \"Role\"='Unknown'", "23514", "CK_ConversationMembers_Role")]
    [InlineData("UPDATE \"ConversationMembers\" SET \"LastReadSeq\"=-1", "23514", "CK_ConversationMembers_LastReadSeqNonNegative")]
    [InlineData("UPDATE \"ConversationMembers\" SET \"LastDeliveredSeq\"=-1, \"LastReadSeq\"=-1", "23514", "CK_ConversationMembers_LastDeliveredSeqNonNegative")]
    [InlineData("UPDATE \"ConversationMembers\" SET \"LastReadSeq\"=2, \"LastDeliveredSeq\"=1", "23514", "CK_ConversationMembers_SeqReadDelivered")]
    [InlineData("UPDATE \"DirectConversations\" SET \"UserLowId\"='00000000-0000-0000-0000-000000000002', \"UserHighId\"='00000000-0000-0000-0000-000000000001'", "23514", "CK_DirectConversations_UserOrder")]
    public async Task InvalidData_IsRejectedByExpectedConstraint(string sql, string state, string constraint)
    {
        var connectionString = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated migrated test database.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        await using var seed = new NpgsqlCommand($"""
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "NormalizedEmail", "DisplayName", "CreatedAt", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{Low}', 'schema_low_{suffix}', 'SCHEMA_LOW_{suffix}', 'SCHEMA_LOW@TEST', 'Low', now(), false, false, false, false, 0),
                   ('{High}', 'schema_high_{suffix}', 'SCHEMA_HIGH_{suffix}', 'SCHEMA_HIGH@TEST', 'High', now(), false, false, false, false, 0);
            INSERT INTO "Friendships" ("UserLowId", "UserHighId", "RequesterUserId", "Status", "CreatedAt") VALUES ('{Low}', '{High}', '{Low}', 'Pending', now());
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "LastMessageSeq", "CreatedAt", "LastActivityAt") VALUES ('{Low}', '{Low}', 'Direct', 0, now(), now());
            INSERT INTO "DirectConversations" ("ConversationId", "UserLowId", "UserHighId") VALUES ('{Low}', '{Low}', '{High}');
            INSERT INTO "ConversationMembers" ("ConversationId", "UserId", "JoinedAt") VALUES ('{Low}', '{Low}', now());
            """, connection, transaction);
        await seed.ExecuteNonQueryAsync();
        // Restrict UPDATE cases to the seeded row; never touch other tests' data.
        if (sql.StartsWith("UPDATE") && !sql.Contains(" WHERE "))
        {
            var key = sql.Contains("\"Friendships\"") || sql.Contains("\"DirectConversations\"") ? "UserLowId"
                : sql.Contains("\"ConversationMembers\"") ? "UserId" : "Id";
            sql += $" WHERE \"{key}\"='{Low}'";
        }
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(state, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
        await transaction.RollbackAsync();
    }
}
