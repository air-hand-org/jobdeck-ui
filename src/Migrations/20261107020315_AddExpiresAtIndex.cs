using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ui.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiresAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions");
        }
    }
}
