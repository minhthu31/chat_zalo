using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupConversationsAndAdminTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupConversations",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupConversations", x => x.ConversationId);
                    table.CheckConstraint("CK_GroupConversations_Name", "\"Name\" ~ '[^[:space:]]'");
                    table.ForeignKey(
                        name: "FK_GroupConversations_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupAdminTransferRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupAdminTransferRequests", x => x.Id);
                    table.CheckConstraint("CK_GroupAdminTransferRequests_DifferentUsers", "\"FromUserId\" <> \"ToUserId\"");
                    table.CheckConstraint("CK_GroupAdminTransferRequests_Resolution", "(\"Status\" = 'Pending') = (\"ResolvedAt\" IS NULL)");
                    table.CheckConstraint("CK_GroupAdminTransferRequests_Status", "\"Status\" IN ('Pending', 'Accepted', 'Rejected', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_GroupAdminTransferRequests_ConversationMembers_Conversation~",
                        columns: x => new { x.ConversationId, x.FromUserId },
                        principalTable: "ConversationMembers",
                        principalColumns: new[] { "ConversationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupAdminTransferRequests_ConversationMembers_Conversatio~1",
                        columns: x => new { x.ConversationId, x.ToUserId },
                        principalTable: "ConversationMembers",
                        principalColumns: new[] { "ConversationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupAdminTransferRequests_GroupConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "GroupConversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_OneActiveAdmin",
                table: "ConversationMembers",
                column: "ConversationId",
                unique: true,
                filter: "\"Role\" = 'Admin' AND \"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GroupAdminTransferRequests_ConversationId_FromUserId",
                table: "GroupAdminTransferRequests",
                columns: new[] { "ConversationId", "FromUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupAdminTransferRequests_ConversationId_ToUserId",
                table: "GroupAdminTransferRequests",
                columns: new[] { "ConversationId", "ToUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupAdminTransferRequests_OnePending",
                table: "GroupAdminTransferRequests",
                column: "ConversationId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_GroupConversations_DeletedAt",
                table: "GroupConversations",
                column: "DeletedAt");

            // A partial unique index prevents two active admins. The deferred check also
            // prevents an active group from committing without an admin or without metadata,
            // checks both source and target groups when memberships change,
            // while allowing group creation and admin transfer to use multiple statements in one transaction.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION check_group_has_one_admin() RETURNS trigger AS $BODY$
                DECLARE
                    group_ids uuid[] := '{}';
                    gid uuid;
                    group_kind text;
                    deleted_at timestamptz;
                    active_admin_count integer;
                    has_metadata boolean;
                BEGIN
                    IF TG_TABLE_NAME = 'Conversations' THEN
                        IF TG_OP = 'DELETE' THEN
                            group_ids := ARRAY[OLD."Id"];
                        ELSIF TG_OP = 'UPDATE' THEN
                            IF OLD."Id" <> NEW."Id" THEN
                                group_ids := ARRAY[OLD."Id", NEW."Id"];
                            ELSE
                                group_ids := ARRAY[NEW."Id"];
                            END IF;
                        ELSE
                            group_ids := ARRAY[NEW."Id"];
                        END IF;
                    ELSIF TG_TABLE_NAME = 'GroupConversations' THEN
                        IF TG_OP = 'DELETE' THEN
                            group_ids := ARRAY[OLD."ConversationId"];
                        ELSIF TG_OP = 'UPDATE' THEN
                            IF OLD."ConversationId" <> NEW."ConversationId" THEN
                                group_ids := ARRAY[OLD."ConversationId", NEW."ConversationId"];
                            ELSE
                                group_ids := ARRAY[NEW."ConversationId"];
                            END IF;
                        ELSE
                            group_ids := ARRAY[NEW."ConversationId"];
                        END IF;
                    ELSIF TG_TABLE_NAME = 'ConversationMembers' THEN
                        IF TG_OP = 'DELETE' THEN
                            group_ids := ARRAY[OLD."ConversationId"];
                        ELSIF TG_OP = 'UPDATE' THEN
                            IF OLD."ConversationId" <> NEW."ConversationId" THEN
                                group_ids := ARRAY[OLD."ConversationId", NEW."ConversationId"];
                            ELSE
                                group_ids := ARRAY[NEW."ConversationId"];
                            END IF;
                        ELSE
                            group_ids := ARRAY[NEW."ConversationId"];
                        END IF;
                    END IF;

                    FOREACH gid IN ARRAY group_ids LOOP
                        SELECT c."Kind" INTO group_kind
                        FROM "Conversations" c
                        WHERE c."Id" = gid;

                        IF NOT FOUND THEN
                            CONTINUE;
                        END IF;

                        SELECT g."DeletedAt" INTO deleted_at
                        FROM "GroupConversations" g
                        WHERE g."ConversationId" = gid
                        FOR UPDATE OF g;

                        has_metadata := FOUND;

                        IF group_kind <> 'Group' THEN
                            IF has_metadata THEN
                                RAISE EXCEPTION 'GroupConversations requires Kind=Group'
                                    USING ERRCODE = '23514', CONSTRAINT = 'CK_GroupConversations_Kind';
                            END IF;
                            CONTINUE;
                        END IF;

                        IF NOT has_metadata THEN
                            RAISE EXCEPTION 'Active group must have metadata and exactly one active admin'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_GroupConversations_OneActiveAdmin';
                        END IF;

                        IF EXISTS (
                            SELECT 1 FROM "ConversationMembers"
                            WHERE "ConversationId" = gid
                              AND "Role" NOT IN ('Admin', 'Member')
                        ) THEN
                            RAISE EXCEPTION 'Group members must have Admin or Member role'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_GroupConversationMembers_Role';
                        END IF;

                        IF deleted_at IS NOT NULL THEN
                            CONTINUE;
                        END IF;

                        SELECT count(*) INTO active_admin_count
                        FROM "ConversationMembers"
                        WHERE "ConversationId" = gid
                          AND "Role" = 'Admin'
                          AND "LeftAt" IS NULL;

                        IF active_admin_count <> 1 THEN
                            RAISE EXCEPTION 'Active group must have exactly one active admin'
                                USING ERRCODE = '23514', CONSTRAINT = 'CK_GroupConversations_OneActiveAdmin';
                        END IF;
                    END LOOP;

                    RETURN NULL;
                END;
                $BODY$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER "TR_GroupConversations_OneActiveAdmin"
                AFTER INSERT OR UPDATE OR DELETE ON "GroupConversations"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();

                CREATE CONSTRAINT TRIGGER "TR_ConversationMembers_OneActiveAdmin"
                AFTER INSERT OR UPDATE OR DELETE ON "ConversationMembers"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();

                CREATE CONSTRAINT TRIGGER "TR_Conversations_GroupKind"
                AFTER INSERT OR UPDATE ON "Conversations"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_Conversations_GroupKind" ON "Conversations";
                DROP TRIGGER IF EXISTS "TR_ConversationMembers_OneActiveAdmin" ON "ConversationMembers";
                DROP TRIGGER IF EXISTS "TR_GroupConversations_OneActiveAdmin" ON "GroupConversations";
                DROP FUNCTION IF EXISTS check_group_has_one_admin();
                """);

            migrationBuilder.DropTable(
                name: "GroupAdminTransferRequests");

            migrationBuilder.DropTable(
                name: "GroupConversations");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMembers_OneActiveAdmin",
                table: "ConversationMembers");
        }
    }
}
