using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingsContactEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ContactEmail",
                table: "Bookings",
                column: "ContactEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_ContactEmail",
                table: "Bookings");
        }
    }
}
