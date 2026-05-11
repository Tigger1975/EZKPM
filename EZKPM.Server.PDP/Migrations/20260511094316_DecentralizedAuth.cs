using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class DecentralizedAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AdminSid",
                table: "VaultRecoveryShares",
                newName: "AdminHashedSid");

            migrationBuilder.RenameColumn(
                name: "RequesterSid",
                table: "VaultRecoveryRequests",
                newName: "TargetHashedSid");

            migrationBuilder.RenameColumn(
                name: "AdSid",
                table: "VaultRecoveryRequests",
                newName: "RequesterHashedSid");

            migrationBuilder.RenameColumn(
                name: "AdSid",
                table: "UserProfiles",
                newName: "HashedSid");

            migrationBuilder.RenameColumn(
                name: "ActorSid",
                table: "RecoveryAuditLogs",
                newName: "ActorHashedSid");

            migrationBuilder.RenameColumn(
                name: "ActorSid",
                table: "AuditLogs",
                newName: "ActorHashedSid");

            migrationBuilder.RenameColumn(
                name: "AdSid",
                table: "AssetAcls",
                newName: "HashedSid");

            migrationBuilder.AddColumn<string>(
                name: "IdentityPublicKey",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PairingInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HashedSid = table.Column<string>(type: "TEXT", nullable: true),
                    HashedUsername = table.Column<string>(type: "TEXT", nullable: true),
                    PairingCodeHash = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairingInvitations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PairingInvitations");

            migrationBuilder.DropColumn(
                name: "IdentityPublicKey",
                table: "UserProfiles");

            migrationBuilder.RenameColumn(
                name: "AdminHashedSid",
                table: "VaultRecoveryShares",
                newName: "AdminSid");

            migrationBuilder.RenameColumn(
                name: "TargetHashedSid",
                table: "VaultRecoveryRequests",
                newName: "RequesterSid");

            migrationBuilder.RenameColumn(
                name: "RequesterHashedSid",
                table: "VaultRecoveryRequests",
                newName: "AdSid");

            migrationBuilder.RenameColumn(
                name: "HashedSid",
                table: "UserProfiles",
                newName: "AdSid");

            migrationBuilder.RenameColumn(
                name: "ActorHashedSid",
                table: "RecoveryAuditLogs",
                newName: "ActorSid");

            migrationBuilder.RenameColumn(
                name: "ActorHashedSid",
                table: "AuditLogs",
                newName: "ActorSid");

            migrationBuilder.RenameColumn(
                name: "HashedSid",
                table: "AssetAcls",
                newName: "AdSid");
        }
    }
}
