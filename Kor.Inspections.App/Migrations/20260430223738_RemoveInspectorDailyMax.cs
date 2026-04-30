using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInspectorDailyMax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyMax",
                table: "Inspectors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyMax",
                table: "Inspectors",
                type: "int",
                nullable: false,
                defaultValue: 8);
        }
    }
}
