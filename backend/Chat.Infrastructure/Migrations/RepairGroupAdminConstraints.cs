using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepairGroupAdminConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CHAT-12 was applied to some databases before its deferred trigger
            // checked missing metadata and both sides of a membership move.
            // Replacing the function and trigger definitions upgrades those databases.
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

                DROP TRIGGER IF EXISTS "TR_GroupConversations_OneActiveAdmin" ON "GroupConversations";
                CREATE CONSTRAINT TRIGGER "TR_GroupConversations_OneActiveAdmin"
                AFTER INSERT OR UPDATE OR DELETE ON "GroupConversations"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();

                DROP TRIGGER IF EXISTS "TR_ConversationMembers_OneActiveAdmin" ON "ConversationMembers";
                CREATE CONSTRAINT TRIGGER "TR_ConversationMembers_OneActiveAdmin"
                AFTER INSERT OR UPDATE OR DELETE ON "ConversationMembers"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();

                DROP TRIGGER IF EXISTS "TR_Conversations_GroupKind" ON "Conversations";
                CREATE CONSTRAINT TRIGGER "TR_Conversations_GroupKind"
                AFTER INSERT OR UPDATE ON "Conversations"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_group_has_one_admin();

                DO $BODY$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Conversations" c
                        LEFT JOIN "GroupConversations" g ON g."ConversationId" = c."Id"
                        WHERE (c."Kind" = 'Group' AND (
                            g."ConversationId" IS NULL OR
                            (g."DeletedAt" IS NULL AND (
                                SELECT count(*) FROM "ConversationMembers" cm
                                WHERE cm."ConversationId" = c."Id"
                                  AND cm."Role" = 'Admin' AND cm."LeftAt" IS NULL
                            ) <> 1)))
                           OR (c."Kind" <> 'Group' AND g."ConversationId" IS NOT NULL)
                    ) THEN
                        RAISE EXCEPTION 'Existing group conversations violate metadata/admin constraints'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_GroupConversations_OneActiveAdmin';
                    END IF;
                END;
                $BODY$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding CHAT-12 migration now defines these same constraints.
            // Keep the integrity checks if this compatibility migration is removed.
        }
    }
}
