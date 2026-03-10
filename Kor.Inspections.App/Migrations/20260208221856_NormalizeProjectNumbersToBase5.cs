using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kor.Inspections.App.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeProjectNumbersToBase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
    -- Normalize ProjectDefaults
    UPDATE [ProjectDefaults]
    SET [ProjectNumber] = SUBSTRING([ProjectNumber], 1, 5)
    WHERE [ProjectNumber] LIKE '[0-9][0-9][0-9][0-9][0-9]%';

    -- Normalize ProjectContacts (if table exists)
    IF OBJECT_ID('dbo.ProjectContacts', 'U') IS NOT NULL
    BEGIN
        UPDATE [ProjectContacts]
        SET [ProjectNumber] = SUBSTRING([ProjectNumber], 1, 5)
        WHERE [ProjectNumber] LIKE '[0-9][0-9][0-9][0-9][0-9]%';
    END

    -- Normalize ProjectAccess (if table exists)
    IF OBJECT_ID('dbo.ProjectAccess', 'U') IS NOT NULL
    BEGIN
        UPDATE [ProjectAccess]
        SET [ProjectNumber] = SUBSTRING([ProjectNumber], 1, 5)
        WHERE [ProjectNumber] LIKE '[0-9][0-9][0-9][0-9][0-9]%';
    END
    ");
        }
   
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
