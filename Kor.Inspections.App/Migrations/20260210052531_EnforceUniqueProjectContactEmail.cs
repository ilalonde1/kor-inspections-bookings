using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueProjectContactEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectContacts_ProjectNumber_EmailDomain",
                table: "ProjectContacts");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectContacts_ProjectNumber_EmailDomain_ContactEmail",
                table: "ProjectContacts",
                columns: new[] { "ProjectNumber", "EmailDomain", "ContactEmail" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectContacts_ProjectNumber_EmailDomain_ContactEmail",
                table: "ProjectContacts");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectContacts_ProjectNumber_EmailDomain",
                table: "ProjectContacts",
                columns: new[] { "ProjectNumber", "EmailDomain" });
        }
    }
}
