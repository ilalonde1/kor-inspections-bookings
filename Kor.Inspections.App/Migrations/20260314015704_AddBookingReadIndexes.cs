using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProjectNumber_StartUtc",
                table: "Bookings",
                columns: new[] { "ProjectNumber", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_ProjectNumber_StartUtc",
                table: "Bookings");
        }
    }
}
