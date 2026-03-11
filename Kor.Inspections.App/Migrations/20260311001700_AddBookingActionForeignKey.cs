using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingActionForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BookingActions_BookingId",
                table: "BookingActions",
                column: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingActions_Bookings_BookingId",
                table: "BookingActions",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "BookingId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingActions_Bookings_BookingId",
                table: "BookingActions");

            migrationBuilder.DropIndex(
                name: "IX_BookingActions_BookingId",
                table: "BookingActions");
        }
    }
}
