using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKForge.Sync.Server.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddEpoch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Epoch",
                table: "Accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Epoch",
                table: "Accounts");
        }
    }
}
