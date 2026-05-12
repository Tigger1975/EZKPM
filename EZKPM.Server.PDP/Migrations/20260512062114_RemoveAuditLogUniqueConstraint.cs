using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAuditLogUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_PreviousEntryHash",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssetId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetHashedSid",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", nullable: true),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Level = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PreviousEntryHash",
                table: "AuditLogs",
                column: "PreviousEntryHash");

            migrationBuilder.CreateIndex(
                name: "IX_ClientLogs_Timestamp",
                table: "ClientLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_PreviousEntryHash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetHashedSid",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssetId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PreviousEntryHash",
                table: "AuditLogs",
                column: "PreviousEntryHash",
                unique: true);
        }
    }
}
