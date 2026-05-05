using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaultAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MetadataHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CipherBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Nonce = table.Column<byte[]>(type: "BLOB", maxLength: 12, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetAcls",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdSid = table.Column<string>(type: "TEXT", nullable: false),
                    PermissionLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    EncryptedKeyShare = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAcls", x => new { x.AssetId, x.AdSid });
                    table.ForeignKey(
                        name: "FK_AssetAcls_VaultAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "VaultAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorSid = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedLogBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Nonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviousEntryHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CurrentEntryHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_VaultAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "VaultAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssetId",
                table: "AuditLogs",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PreviousEntryHash",
                table: "AuditLogs",
                column: "PreviousEntryHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaultAssets_MetadataHash",
                table: "VaultAssets",
                column: "MetadataHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetAcls");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "VaultAssets");
        }
    }
}
