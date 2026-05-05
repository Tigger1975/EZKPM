using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class RecoveryEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    AdSid = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedMasterKeyBackup = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.AdSid);
                });

            migrationBuilder.CreateTable(
                name: "VaultRecoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdSid = table.Column<string>(type: "TEXT", nullable: false),
                    EphemeralUserPubKey = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredShares = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultRecoveryRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaultRecoveryShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoveryRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdminSid = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedShareBlob = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultRecoveryShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaultRecoveryShares_VaultRecoveryRequests_RecoveryRequestId",
                        column: x => x.RecoveryRequestId,
                        principalTable: "VaultRecoveryRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultRecoveryShares_RecoveryRequestId",
                table: "VaultRecoveryShares",
                column: "RecoveryRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "VaultRecoveryShares");

            migrationBuilder.DropTable(
                name: "VaultRecoveryRequests");
        }
    }
}
