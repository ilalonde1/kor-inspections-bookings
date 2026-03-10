using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveBookingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_NoDuplicateActiveSlot",
                table: "Bookings",
                columns: new[] { "ProjectNumber", "ContactEmail", "StartUtc" },
                unique: true,
                filter: "[Status] != 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_NoDuplicateActiveSlot",
                table: "Bookings");
        }
    }
}
