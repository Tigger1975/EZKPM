using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonIdToUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "UserProfiles");
        }
    }
}
