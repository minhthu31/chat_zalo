using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Chat.Infrastructure.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20261121153000_AddMessageUserReceipts")]
public partial class AddMessageUserReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MessageUserReceipts",
            columns: table => new
            {
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MessageUserReceipts", x => new { x.MessageId, x.UserId });
                table.ForeignKey("FK_MessageUserReceipts_Messages_MessageId", x => x.MessageId, "Messages", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_MessageUserReceipts_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_MessageUserReceipts_ReadRequiresDelivered", "\"ReadAt\" IS NULL OR \"DeliveredAt\" IS NOT NULL");
            });
        migrationBuilder.CreateIndex(name: "IX_MessageUserReceipts_UserId", table: "MessageUserReceipts", column: "UserId");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "MessageUserReceipts");
}
