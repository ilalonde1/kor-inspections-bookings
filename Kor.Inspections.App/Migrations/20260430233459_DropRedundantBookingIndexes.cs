using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class DropRedundantBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_ProjectNumber",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Status",
                table: "Bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProjectNumber",
                table: "Bookings",
                column: "ProjectNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status",
                table: "Bookings",
                column: "Status");
        }
    }
}
