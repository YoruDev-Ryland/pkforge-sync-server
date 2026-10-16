using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PKForge.Sync.Server.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ReleasedValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleasedValue",
                table: "Items",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleasedValue",
                table: "Items");
        }
    }
}
