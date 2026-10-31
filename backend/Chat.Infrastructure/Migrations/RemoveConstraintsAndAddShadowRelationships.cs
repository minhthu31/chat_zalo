using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueEmailConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserBlocks_SelfBlock",
                table: "UserBlocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Friendships_RequesterUserId",
                table: "Friendships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Friendships_Status",
                table: "Friendships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Friendships_UserOrder",
                table: "Friendships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DirectConversations_UserOrder",
                table: "DirectConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversations_Kind",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversations_LastMessageSeq",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConversationMembers_LastDeliveredSeqNonNegative",
                table: "ConversationMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConversationMembers_LastReadSeqNonNegative",
                table: "ConversationMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConversationMembers_Role",
                table: "ConversationMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConversationMembers_SeqReadDelivered",
                table: "ConversationMembers");

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId1",
                table: "Messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ConversationMembers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Member");

            migrationBuilder.AlterColumn<long>(
                name: "LastReadSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "LastDeliveredSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "JoinedSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId1",
                table: "ConversationMembers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId1",
                table: "Messages",
                column: "ConversationId1");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_ConversationId1",
                table: "ConversationMembers",
                column: "ConversationId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMembers_Conversations_ConversationId1",
                table: "ConversationMembers",
                column: "ConversationId1",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Conversations_ConversationId1",
                table: "Messages",
                column: "ConversationId1",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMembers_Conversations_ConversationId1",
                table: "ConversationMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Conversations_ConversationId1",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId1",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMembers_ConversationId1",
                table: "ConversationMembers");

            migrationBuilder.DropColumn(
                name: "ConversationId1",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ConversationId1",
                table: "ConversationMembers");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ConversationMembers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Member",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<long>(
                name: "LastReadSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "LastDeliveredSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "JoinedSeq",
                table: "ConversationMembers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserBlocks_SelfBlock",
                table: "UserBlocks",
                sql: "\"BlockerUserId\" <> \"BlockedUserId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Friendships_RequesterUserId",
                table: "Friendships",
                sql: "(\"RequesterUserId\" = \"UserLowId\") OR (\"RequesterUserId\" = \"UserHighId\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Friendships_Status",
                table: "Friendships",
                sql: "\"Status\" IN ('Pending', 'Accepted', 'Rejected', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Friendships_UserOrder",
                table: "Friendships",
                sql: "\"UserLowId\" < \"UserHighId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DirectConversations_UserOrder",
                table: "DirectConversations",
                sql: "\"UserLowId\" < \"UserHighId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversations_Kind",
                table: "Conversations",
                sql: "\"Kind\" IN ('Direct', 'Group')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversations_LastMessageSeq",
                table: "Conversations",
                sql: "\"LastMessageSeq\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConversationMembers_LastDeliveredSeqNonNegative",
                table: "ConversationMembers",
                sql: "\"LastDeliveredSeq\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConversationMembers_LastReadSeqNonNegative",
                table: "ConversationMembers",
                sql: "\"LastReadSeq\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConversationMembers_Role",
                table: "ConversationMembers",
                sql: "\"Role\" IN ('Member', 'Admin', 'Owner')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConversationMembers_SeqReadDelivered",
                table: "ConversationMembers",
                sql: "\"LastReadSeq\" <= \"LastDeliveredSeq\"");
        }
    }
}
