using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3FilesProfilesNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AvatarFileId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ConversationNotificationSettings",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsMuted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationNotificationSettings", x => new { x.ConversationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ConversationNotificationSettings_ConversationMembers_Conver~",
                        columns: x => new { x.ConversationId, x.UserId },
                        principalTable: "ConversationMembers",
                        principalColumns: new[] { "ConversationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.CheckConstraint("CK_StoredFiles_ContentTypeNonEmpty", "length(btrim(\"ContentType\")) > 0");
                    table.CheckConstraint("CK_StoredFiles_FileNameNonEmpty", "length(btrim(\"FileName\")) > 0");
                    table.CheckConstraint("CK_StoredFiles_SizePositive", "\"SizeBytes\" > 0");
                    table.CheckConstraint("CK_StoredFiles_StorageKeyNonEmpty", "length(btrim(\"StorageKey\")) > 0");
                    table.CheckConstraint("CK_StoredFiles_UsageStatusAndSize", "(\"UsageType\" = 'Avatar' AND \"ConversationId\" IS NULL\n    AND \"Status\" IN ('Pending', 'Active', 'Inactive')\n    AND \"SizeBytes\" <= 2097152)\nOR\n(\"UsageType\" = 'Attachment' AND \"ConversationId\" IS NOT NULL\n    AND \"Status\" IN ('Pending', 'Attached')\n    AND \"SizeBytes\" <= 10485760)");
                    table.ForeignKey(
                        name: "FK_StoredFiles_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoredFiles_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => new { x.MessageId, x.StoredFileId });
                    table.ForeignKey(
                        name: "FK_MessageAttachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_StoredFiles_StoredFileId",
                        column: x => x.StoredFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_AvatarFileId",
                table: "Users",
                column: "AvatarFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationNotificationSettings_UserId",
                table: "ConversationNotificationSettings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_StoredFileId",
                table: "MessageAttachments",
                column: "StoredFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ConversationId",
                table: "StoredFiles",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Status_CreatedAt",
                table: "StoredFiles",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_StorageKey",
                table: "StoredFiles",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_UploadedByUserId",
                table: "StoredFiles",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_StoredFiles_AvatarFileId",
                table: "Users",
                column: "AvatarFileId",
                principalTable: "StoredFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION chat_s3_validate_avatar() RETURNS trigger AS $$
                BEGIN
                    IF NEW."AvatarFileId" IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM "StoredFiles" f
                        WHERE f."Id" = NEW."AvatarFileId"
                          AND f."UploadedByUserId" = NEW."Id"
                          AND f."UsageType" = 'Avatar'
                          AND f."Status" = 'Active'
                    ) THEN
                        RAISE EXCEPTION 'Avatar must be an active file uploaded by its owner'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER "TR_Users_ValidAvatar"
                AFTER INSERT OR UPDATE ON "Users"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION chat_s3_validate_avatar();

                CREATE FUNCTION chat_s3_validate_file_links() RETURNS trigger AS $$
                DECLARE file_id uuid;
                BEGIN
                    file_id := NEW."Id";
                    IF NOT EXISTS (SELECT 1 FROM "StoredFiles" WHERE "Id" = file_id) THEN
                        RETURN NULL;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "Users" u JOIN "StoredFiles" f ON f."Id" = u."AvatarFileId"
                        WHERE f."Id" = file_id AND (f."UploadedByUserId" <> u."Id"
                            OR f."UsageType" <> 'Avatar' OR f."Status" <> 'Active')
                    ) OR EXISTS (
                        SELECT 1 FROM "MessageAttachments" a
                        JOIN "Messages" m ON m."Id" = a."MessageId"
                        JOIN "StoredFiles" f ON f."Id" = a."StoredFileId"
                        WHERE f."Id" = file_id AND (f."ConversationId" <> m."ConversationId"
                            OR f."UploadedByUserId" <> m."SenderUserId"
                            OR f."UsageType" <> 'Attachment' OR f."Status" <> 'Attached')
                    ) OR EXISTS (
                        SELECT 1 FROM "StoredFiles" f WHERE f."Id" = file_id
                          AND f."Status" = 'Attached'
                          AND NOT EXISTS (SELECT 1 FROM "MessageAttachments" a WHERE a."StoredFileId" = f."Id")
                    ) THEN
                        RAISE EXCEPTION 'Stored file has an invalid avatar or message link'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER "TR_StoredFiles_ValidLinks"
                AFTER INSERT OR UPDATE ON "StoredFiles"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION chat_s3_validate_file_links();

                CREATE FUNCTION chat_s3_validate_message_attachment() RETURNS trigger AS $$
                DECLARE link_id uuid;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        link_id := OLD."StoredFileId";
                    ELSE
                        link_id := NEW."StoredFileId";
                    END IF;

                    -- Serialize concurrent attachments to the same message before counting.
                    PERFORM 1 FROM "Messages" m
                    JOIN "MessageAttachments" a ON a."MessageId" = m."Id"
                    WHERE a."StoredFileId" = link_id
                    FOR UPDATE OF m;

                    IF EXISTS (
                        SELECT 1 FROM "MessageAttachments" a
                        JOIN "Messages" m ON m."Id" = a."MessageId"
                        JOIN "StoredFiles" f ON f."Id" = a."StoredFileId"
                        WHERE a."StoredFileId" = link_id
                          AND (f."ConversationId" <> m."ConversationId"
                            OR f."UploadedByUserId" <> m."SenderUserId"
                            OR f."UsageType" <> 'Attachment' OR f."Status" <> 'Attached')
                    ) OR EXISTS (
                        SELECT 1 FROM "MessageAttachments" a
                        WHERE a."StoredFileId" = link_id
                          AND (SELECT count(*) FROM "MessageAttachments" a2 WHERE a2."MessageId" = a."MessageId") > 5
                    ) OR EXISTS (
                        SELECT 1 FROM "StoredFiles" f WHERE f."Id" = link_id
                          AND f."Status" = 'Attached'
                          AND NOT EXISTS (SELECT 1 FROM "MessageAttachments" a WHERE a."StoredFileId" = f."Id")
                    ) THEN
                        RAISE EXCEPTION 'Invalid message attachment or more than five files on a message'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER "TR_MessageAttachments_ValidLink"
                AFTER INSERT OR UPDATE OR DELETE ON "MessageAttachments"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION chat_s3_validate_message_attachment();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION chat_s3_validate_message_attachment() CASCADE;
                DROP FUNCTION chat_s3_validate_file_links() CASCADE;
                DROP FUNCTION chat_s3_validate_avatar() CASCADE;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Users_StoredFiles_AvatarFileId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "ConversationNotificationSettings");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_Users_AvatarFileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarFileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Users");
        }
    }
}
