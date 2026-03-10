using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueInspectorEmailIndex : Migration
    {
        // Before applying in production, verify there are no duplicate inspector emails:
        // SELECT Email, COUNT(*) FROM Inspectors GROUP BY Email HAVING COUNT(*) > 1
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Inspectors_UniqueEmail",
                table: "Inspectors",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Inspectors_UniqueEmail",
                table: "Inspectors");
        }
    }
}
