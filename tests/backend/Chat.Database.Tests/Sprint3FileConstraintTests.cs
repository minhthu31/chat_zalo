using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public sealed class Sprint3FileConstraintTests
{
    private static string TestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated test database.");
        var database = new NpgsqlConnectionStringBuilder(value).Database ?? string.Empty;
        if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sprint 3 schema tests require a dedicated _test or _review database.");
        }

        return value;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(Guid sender, Guid otherUser, Guid conversation, Guid otherConversation, Guid message, Guid file)>
        SeedAttachedMessageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sender = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        var otherConversation = Guid.NewGuid();
        var message = Guid.NewGuid();
        var file = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "DisplayName", "CreatedAt",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{sender}', 's3_{sender:N}', 'S3_{sender:N}', 'Sender', now(), false, false, false, false, 0),
                   ('{otherUser}', 's3_{otherUser:N}', 'S3_{otherUser:N}', 'Other', now(), false, false, false, false, 0);
            INSERT INTO "Conversations" ("Id", "CreatedByUserId", "Kind", "LastMessageSeq", "CreatedAt", "LastActivityAt")
            VALUES ('{conversation}', '{sender}', 'Direct', 1, now(), now()),
                   ('{otherConversation}', '{sender}', 'Direct', 0, now(), now());
            INSERT INTO "Messages" ("Id", "ConversationId", "SenderUserId", "ClientMessageId", "Seq", "Body", "CreatedAt")
            VALUES ('{message}', '{conversation}', '{sender}', '{clientMessageId}', 1, 'File message', now());
            INSERT INTO "StoredFiles" ("Id", "UploadedByUserId", "ConversationId", "UsageType", "Status",
                "StorageKey", "FileName", "ContentType", "SizeBytes", "CreatedAt")
            VALUES ('{file}', '{sender}', '{conversation}', 'Attachment', 'Attached', 's3/{file:N}',
                'file.pdf', 'application/pdf', 100, now());
            INSERT INTO "MessageAttachments" ("MessageId", "StoredFileId") VALUES ('{message}', '{file}');
            SET CONSTRAINTS ALL IMMEDIATE;
            """);

        return (sender, otherUser, conversation, otherConversation, message, file);
    }

    [Fact]
    public async Task ChangingStoredFileId_CannotLeavePreviousAttachedFileOrphaned()
    {
        await using var connection = new NpgsqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var seed = await SeedAttachedMessageAsync(connection, transaction);
        var replacement = Guid.NewGuid();

        await ExecuteAsync(connection, transaction, $"""
            SET CONSTRAINTS ALL DEFERRED;
            INSERT INTO "StoredFiles" ("Id", "UploadedByUserId", "ConversationId", "UsageType", "Status",
                "StorageKey", "FileName", "ContentType", "SizeBytes", "CreatedAt")
            VALUES ('{replacement}', '{seed.sender}', '{seed.conversation}', 'Attachment', 'Attached',
                's3/{replacement:N}', 'replacement.pdf', 'application/pdf', 100, now());
            UPDATE "MessageAttachments" SET "StoredFileId" = '{replacement}' WHERE "MessageId" = '{seed.message}';
            """);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE;"));
        Assert.Equal("23514", error.SqlState);
        Assert.Contains("Previous attached file", error.MessageText);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("SenderUserId")]
    [InlineData("ConversationId")]
    public async Task AttachedMessage_CannotChangeSenderOrConversation(string column)
    {
        await using var connection = new NpgsqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var seed = await SeedAttachedMessageAsync(connection, transaction);
        var replacement = column == "SenderUserId" ? seed.otherUser : seed.otherConversation;

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction,
                $"UPDATE \"Messages\" SET \"{column}\" = '{replacement}' WHERE \"Id\" = '{seed.message}';"));
        Assert.Equal("23514", error.SqlState);
        Assert.Contains("immutable", error.MessageText);
        await transaction.RollbackAsync();
    }
}
