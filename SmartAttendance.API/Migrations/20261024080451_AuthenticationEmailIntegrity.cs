using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.API.Migrations;

public partial class AuthenticationEmailIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("This preservation migration requires SQL Server.");
        // Fail before touching schema: never truncate, merge, delete or promote identities.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [Users] WHERE DATALENGTH(UPPER(LTRIM(RTRIM([Email])))) > 508)
                THROW 51020, 'An email identity exceeds the normalized key size. Review data before migration.', 1;
            IF EXISTS (
                SELECT UPPER(LTRIM(RTRIM([Email]))) COLLATE Latin1_General_100_CI_AS
                FROM [Users] WHERE LTRIM(RTRIM([Email])) <> ''
                GROUP BY UPPER(LTRIM(RTRIM([Email]))) COLLATE Latin1_General_100_CI_AS HAVING COUNT(*) > 1)
                THROW 51021, 'Duplicate email identities require reviewed reconciliation before migration.', 1;
            """);
        migrationBuilder.AddColumn<string>(name: "NormalizedEmail", table: "Users", type: "nvarchar(254)",
            maxLength: 254, nullable: true, collation: "Latin1_General_100_CI_AS");
        migrationBuilder.Sql("""
            EXEC(N'UPDATE [Users] SET [NormalizedEmail] =
                CASE WHEN LTRIM(RTRIM([Email])) = '''' THEN NULL
                ELSE CONVERT(nvarchar(254), UPPER(LTRIM(RTRIM([Email])))) END;');
            """);
        migrationBuilder.CreateIndex(name: "IX_Users_NormalizedEmail", table: "Users", column: "NormalizedEmail",
            unique: true, filter: "[NormalizedEmail] IS NOT NULL");
        migrationBuilder.AddCheckConstraint(name: "CK_Users_NormalizedEmail", table: "Users", sql:
            "([NormalizedEmail] IS NULL AND LTRIM(RTRIM([Email])) = '') OR ([NormalizedEmail] IS NOT NULL AND LTRIM(RTRIM([Email])) <> '' AND [NormalizedEmail] <> '')");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Automatic rollback would remove the email identity security constraint. Use an approved recovery or forward migration.");
    }
}
